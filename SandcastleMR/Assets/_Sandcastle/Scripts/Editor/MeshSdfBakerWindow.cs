using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Sandcastle;

namespace SandcastleEditor
{
    /// <summary>
    /// 把一个 mesh 烘焙成 3D SDF 纹理。
    /// 用法：在 Project 窗口选中一个 mesh / fbx / 模型 prefab，
    ///       菜单 Tools → Sandcastle → Bake Mesh SDF。
    /// 
    /// 算法：
    /// - 取 mesh 包围盒 + padding
    /// - 在每个体素中心计算到所有三角形的最近距离
    /// - 用射线穿越法判断内外（奇数次穿越 = 内部）
    /// - 输出 RFloat Texture3D
    /// </summary>
    public class MeshSdfBakerWindow : EditorWindow
    {
        [MenuItem("Tools/Sandcastle/Bake Mesh SDF")]
        public static void Open()
        {
            GetWindow<MeshSdfBakerWindow>("Mesh SDF Baker");
        }

        private GameObject sourceObject;
        private Mesh sourceMesh;
        private int resolution = 32;
        private float paddingRatio = 0.1f;
        private string outputName = "MeshSdf";

        void OnGUI()
        {
            GUILayout.Label("Mesh SDF Baker", EditorStyles.boldLabel);

            sourceObject = (GameObject)EditorGUILayout.ObjectField("Source GameObject (prefab/fbx)", sourceObject, typeof(GameObject), false);
            sourceMesh = (Mesh)EditorGUILayout.ObjectField("Source Mesh (alt input)", sourceMesh, typeof(Mesh), false);
            resolution = EditorGUILayout.IntSlider("Resolution", resolution, 8, 96);
            paddingRatio = EditorGUILayout.Slider("Padding Ratio", paddingRatio, 0f, 0.5f);
            outputName = EditorGUILayout.TextField("Output Name", outputName);

            EditorGUILayout.HelpBox(
                "Resolution³ × triangleCount = bake time。\n" +
                "32³ + 5000 tris ≈ 30~60s\n" +
                "48³ + 10000 tris ≈ 数分钟", MessageType.Info);

            if (GUILayout.Button("Bake"))
            {
                Mesh m = ResolveMesh();
                if (m == null)
                {
                    EditorUtility.DisplayDialog("Error", "请提供 Source GameObject 或 Source Mesh", "OK");
                    return;
                }
                Bake(m);
            }
        }

        Mesh ResolveMesh()
        {
            if (sourceMesh != null) return sourceMesh;
            if (sourceObject != null)
            {
                var mf = sourceObject.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
                var smr = sourceObject.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null) return smr.sharedMesh;
            }
            return null;
        }

        void Bake(Mesh mesh)
        {
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;

            // 包围盒 + padding
            Bounds b = mesh.bounds;
            Vector3 pad = b.size * paddingRatio;
            b.Expand(pad * 2f);

            int rx = resolution, ry = resolution, rz = resolution;
            float[] sdfData = new float[rx * ry * rz];

            Vector3 min = b.min;
            Vector3 size = b.size;
            float dx = size.x / (rx - 1);
            float dy = size.y / (ry - 1);
            float dz = size.z / (rz - 1);

            int total = rx * ry * rz;
            int done = 0;

            for (int z = 0; z < rz; z++)
            {
                for (int y = 0; y < ry; y++)
                {
                    for (int x = 0; x < rx; x++)
                    {
                        Vector3 p = new Vector3(min.x + x * dx, min.y + y * dy, min.z + z * dz);

                        // 最近距离
                        float minDist2 = float.PositiveInfinity;
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            Vector3 a = verts[tris[t]];
                            Vector3 vB = verts[tris[t + 1]];
                            Vector3 c = verts[tris[t + 2]];
                            float d2 = PointTriangleDistanceSq(p, a, vB, c);
                            if (d2 < minDist2) minDist2 = d2;
                        }
                        float dist = Mathf.Sqrt(minDist2);

                        // 内外判断：从 p 沿 +X 射线，统计与三角形相交次数
                        int hits = 0;
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            Vector3 a = verts[tris[t]];
                            Vector3 vB = verts[tris[t + 1]];
                            Vector3 c = verts[tris[t + 2]];
                            if (RayHitsTriangle(p, Vector3.right, a, vB, c))
                                hits++;
                        }
                        bool inside = (hits & 1) == 1;
                        if (inside) dist = -dist;

                        sdfData[x + y * rx + z * rx * ry] = dist;

                        done++;
                        if (done % 256 == 0)
                        {
                            float prog = (float)done / total;
                            if (EditorUtility.DisplayCancelableProgressBar(
                                "Baking SDF",
                                $"{done}/{total} voxels",
                                prog))
                            {
                                EditorUtility.ClearProgressBar();
                                return;
                            }
                        }
                    }
                }
            }
            EditorUtility.ClearProgressBar();

            // 创建 Texture3D
            var tex = new Texture3D(rx, ry, rz, TextureFormat.RFloat, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color[] cols = new Color[rx * ry * rz];
            for (int i = 0; i < cols.Length; i++)
                cols[i] = new Color(sdfData[i], 0, 0, 0);
            tex.SetPixels(cols);
            tex.Apply(false, false); // false = 保留 CPU 数据，方便运行时 GetPixels

            // 创建 ScriptableObject
            var asset = ScriptableObject.CreateInstance<MeshSdfAsset>();
            asset.bounds = b;
            asset.resolution = new Vector3Int(rx, ry, rz);
            asset.sdfTex = tex;

            // 保存
            // 保存到 Resources 下，运行时能被 Resources.LoadAll 发现
            string folder = "Assets/_Sandcastle/Resources/Models";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Sandcastle/Resources"))
                    AssetDatabase.CreateFolder("Assets/_Sandcastle", "Resources");
                AssetDatabase.CreateFolder("Assets/_Sandcastle/Resources", "Models");
            }
            string path = $"{folder}/{outputName}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.AddObjectToAsset(tex, asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = asset;
            Debug.Log($"[MeshSdfBaker] Saved to {path}, bounds={b}, resolution={rx}");
        }

        // 点到三角形距离平方
        static float PointTriangleDistanceSq(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return (p - a).sqrMagnitude;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return (p - b).sqrMagnitude;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                float v = d1 / (d1 - d3);
                Vector3 closest = a + v * ab;
                return (p - closest).sqrMagnitude;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return (p - c).sqrMagnitude;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                float w = d2 / (d2 - d6);
                Vector3 closest = a + w * ac;
                return (p - closest).sqrMagnitude;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                Vector3 closest = b + w * (c - b);
                return (p - closest).sqrMagnitude;
            }

            // 在三角形内
            float denom = 1f / (va + vb + vc);
            float vV = vb * denom;
            float vW = vc * denom;
            Vector3 closestInside = a + ab * vV + ac * vW;
            return (p - closestInside).sqrMagnitude;
        }

        // Möller–Trumbore 射线-三角形求交（只关心是否命中且 t > 0）
        static bool RayHitsTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c)
        {
            const float eps = 1e-7f;
            Vector3 e1 = b - a;
            Vector3 e2 = c - a;
            Vector3 h = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, h);
            if (det > -eps && det < eps) return false;
            float invDet = 1f / det;
            Vector3 s = origin - a;
            float u = invDet * Vector3.Dot(s, h);
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = invDet * Vector3.Dot(dir, q);
            if (v < 0 || u + v > 1) return false;
            float t = invDet * Vector3.Dot(e2, q);
            return t > eps;
        }
    }
}
