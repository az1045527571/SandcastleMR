using System.Collections.Generic;
using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 局部 SDF 体积：
    /// - 96³ 体素，覆盖 40cm × 40cm × 25cm（每格 ~4mm，Y 轴每格 ~2.6mm）
    /// - 维护一组 SdfPiece 的形状，事件触发时重新合并 + Marching Cubes 提取 mesh
    /// - 输出到 RebuildTarget（GameObject 上的 MeshFilter/MeshRenderer）
    /// 
    /// 性能策略：
    /// - 不每帧更新，只在 piece 增/减/海浪事件时调 RebuildMesh()
    /// - CPU MC 约 30~50ms (96³)，PC 端开发足够，后期迁 Compute Shader
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class SdfVolume : MonoBehaviour
    {
        [Header("体积")]
        public Vector3 size = new Vector3(5f, 2f, 5f);
        public int resolutionX = 96;
        public int resolutionY = 32;
        public int resolutionZ = 96;

        [Header("Smooth Union")]
        [Tooltip("smooth min 平滑系数。越大融合越柔和但会损失细节")]
        public float smoothK = 0.2f;

        [Header("ISO 表面")]
        [Tooltip("等值面阈值。0 = SDF 表面")]
        public float isoLevel = 0f;

        [Header("地形融入")]
        [Tooltip("是否将 SandTerrain 高度场作为基础 SDF")]
        public bool includeTerrain = true;
        public float terrainSmoothK = 0.15f;

        // 体素数据
        private float[] _sdf;
        private int Nx => resolutionX + 1;
        private int Ny => resolutionY + 1;
        private int Nz => resolutionZ + 1;

        // 注册的 SDF 形状
        private readonly List<SdfPiece> _pieces = new List<SdfPiece>();
        private SandTerrain _terrain;

        private Mesh _mesh;
        private MeshFilter _meshFilter;

        // 输出 buffer（避免每次 alloc）
        private List<Vector3> _vertBuf = new List<Vector3>(8192);
        private List<int> _triBuf = new List<int>(16384);
        private List<Color> _colorBuf = new List<Color>(8192);
        private List<Vector3> _normalBuf = new List<Vector3>(8192);

        void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _mesh = new Mesh();
            _mesh.name = "SdfMesh";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _meshFilter.sharedMesh = _mesh;

            _sdf = new float[Nx * Ny * Nz];
            _terrain = FindObjectOfType<SandTerrain>();
        }

        void Start()
        {
            // 初始建一次，即使没有球，地形 SDF 也会填充地面 mesh
            RebuildMesh();
        }

        public void Register(SdfPiece p)
        {
            if (!_pieces.Contains(p)) _pieces.Add(p);
            RebuildMesh();
        }

        public void Unregister(SdfPiece p)
        {
            if (_pieces.Remove(p)) RebuildMesh();
        }

        /// <summary>世界坐标 → 体素本地坐标（[0,size] 范围）。</summary>
        public Vector3 WorldToLocal(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            // 把原点从中心移到角落
            return local + size * 0.5f;
        }

        public Vector3 LocalToWorld(Vector3 localPos)
        {
            Vector3 centered = localPos - size * 0.5f;
            return transform.TransformPoint(centered);
        }

        /// <summary>
        /// 重新计算整个 SDF 并提取 mesh。
        /// </summary>
        public void RebuildMesh()
        {
            EvaluateSdf();
            ExtractMesh();
            Debug.Log($"[SdfVolume] Pieces={_pieces.Count}, Verts={_vertBuf.Count}, Tris={_triBuf.Count/3}, Bounds={_mesh.bounds}");
        }

        /// <summary>对每个体素求所有 piece 的 smooth min。</summary>
        void EvaluateSdf()
        {
            float dx = size.x / resolutionX;
            float dy = size.y / resolutionY;
            float dz = size.z / resolutionZ;

            for (int z = 0; z < Nz; z++)
            {
                for (int y = 0; y < Ny; y++)
                {
                    for (int x = 0; x < Nx; x++)
                    {
                        Vector3 localPos = new Vector3(x * dx, y * dy, z * dz);
                        Vector3 worldPos = LocalToWorld(localPos);

                        // 基础 SDF ：高度场 (worldY - terrainHeight)。负值=在沙下，正值=在沙上
                        float d = float.PositiveInfinity;
                        if (includeTerrain && _terrain != null)
                        {
                            float groundY = _terrain.SampleHeight(worldPos);
                            d = worldPos.y - groundY;
                        }

                        for (int i = 0; i < _pieces.Count; i++)
                        {
                            float di = _pieces[i].SampleSdf(worldPos);
                            d = SmoothMin(d, di, smoothK);
                        }
                        _sdf[Index(x, y, z)] = d;
                    }
                }
            }
        }

        /// <summary>多项式 smooth min（IQ 公式）。</summary>
        public static float SmoothMin(float a, float b, float k)
        {
            if (float.IsPositiveInfinity(a)) return b;
            if (float.IsPositiveInfinity(b)) return a;
            if (k <= 0f) return Mathf.Min(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        int Index(int x, int y, int z) => x + y * Nx + z * Nx * Ny;

        /// <summary>三线性采样 SDF（入参 = 体积局部坐标，角落原点 [0,size]）</summary>
        float SampleSdfAtLocal(Vector3 localPos)
        {
            float dx = size.x / resolutionX;
            float dy = size.y / resolutionY;
            float dz = size.z / resolutionZ;
            float fx = Mathf.Clamp(localPos.x / dx, 0, resolutionX);
            float fy = Mathf.Clamp(localPos.y / dy, 0, resolutionY);
            float fz = Mathf.Clamp(localPos.z / dz, 0, resolutionZ);
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            int z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, resolutionX);
            int y1 = Mathf.Min(y0 + 1, resolutionY);
            int z1 = Mathf.Min(z0 + 1, resolutionZ);
            float tx = fx - x0;
            float ty = fy - y0;
            float tz = fz - z0;
            float c000 = _sdf[Index(x0, y0, z0)];
            float c100 = _sdf[Index(x1, y0, z0)];
            float c010 = _sdf[Index(x0, y1, z0)];
            float c110 = _sdf[Index(x1, y1, z0)];
            float c001 = _sdf[Index(x0, y0, z1)];
            float c101 = _sdf[Index(x1, y0, z1)];
            float c011 = _sdf[Index(x0, y1, z1)];
            float c111 = _sdf[Index(x1, y1, z1)];
            float c00 = Mathf.Lerp(c000, c100, tx);
            float c10 = Mathf.Lerp(c010, c110, tx);
            float c01 = Mathf.Lerp(c001, c101, tx);
            float c11 = Mathf.Lerp(c011, c111, tx);
            float c0 = Mathf.Lerp(c00, c10, ty);
            float c1 = Mathf.Lerp(c01, c11, ty);
            return Mathf.Lerp(c0, c1, tz);
        }

        /// <summary>SDF 梯度作为顶点法线，实现平滑着色</summary>
        Vector3 SdfGradient(Vector3 localPos)
        {
            float eps = Mathf.Min(size.x / resolutionX, size.y / resolutionY) * 0.5f;
            float gx = SampleSdfAtLocal(localPos + new Vector3(eps, 0, 0)) - SampleSdfAtLocal(localPos - new Vector3(eps, 0, 0));
            float gy = SampleSdfAtLocal(localPos + new Vector3(0, eps, 0)) - SampleSdfAtLocal(localPos - new Vector3(0, eps, 0));
            float gz = SampleSdfAtLocal(localPos + new Vector3(0, 0, eps)) - SampleSdfAtLocal(localPos - new Vector3(0, 0, eps));
            Vector3 g = new Vector3(gx, gy, gz);
            float m = g.magnitude;
            if (m < 1e-6f) return Vector3.up;
            return g / m;
        }

        /// <summary>Marching Cubes 提取 mesh（CPU 版）。</summary>
        void ExtractMesh()
        {
            _vertBuf.Clear();
            _triBuf.Clear();
            _colorBuf.Clear();
            _normalBuf.Clear();

            float dx = size.x / resolutionX;
            float dy = size.y / resolutionY;
            float dz = size.z / resolutionZ;

            // 8 个 cube 顶点的 SDF 值
            float[] cubeVal = new float[8];
            // 12 条边的插值后顶点位置（本地坐标）
            Vector3[] edgeVert = new Vector3[12];

            for (int z = 0; z < resolutionZ; z++)
            {
                for (int y = 0; y < resolutionY; y++)
                {
                    for (int x = 0; x < resolutionX; x++)
                    {
                        // 取 8 个角的 SDF
                        for (int v = 0; v < 8; v++)
                        {
                            int vx = x + MarchingCubesTables.VertexOffset[v, 0];
                            int vy = y + MarchingCubesTables.VertexOffset[v, 1];
                            int vz = z + MarchingCubesTables.VertexOffset[v, 2];
                            cubeVal[v] = _sdf[Index(vx, vy, vz)];
                        }

                        // 决定 cube 配置 index
                        int ci = 0;
                        for (int v = 0; v < 8; v++)
                            if (cubeVal[v] < isoLevel) ci |= 1 << v;

                        int edgeMask = MarchingCubesTables.EdgeTable[ci];
                        if (edgeMask == 0) continue;

                        // 对每条相交边做线性插值
                        for (int e = 0; e < 12; e++)
                        {
                            if ((edgeMask & (1 << e)) == 0) continue;
                            int a = MarchingCubesTables.EdgeVertexIndex[e, 0];
                            int b = MarchingCubesTables.EdgeVertexIndex[e, 1];
                            float va = cubeVal[a];
                            float vb = cubeVal[b];
                            float t = Mathf.Abs(va - vb) < 1e-6f ? 0.5f
                                    : (isoLevel - va) / (vb - va);

                            Vector3 pa = new Vector3(
                                (x + MarchingCubesTables.VertexOffset[a, 0]) * dx,
                                (y + MarchingCubesTables.VertexOffset[a, 1]) * dy,
                                (z + MarchingCubesTables.VertexOffset[a, 2]) * dz);
                            Vector3 pb = new Vector3(
                                (x + MarchingCubesTables.VertexOffset[b, 0]) * dx,
                                (y + MarchingCubesTables.VertexOffset[b, 1]) * dy,
                                (z + MarchingCubesTables.VertexOffset[b, 2]) * dz);

                            edgeVert[e] = Vector3.Lerp(pa, pb, t);
                        }

                        // 输出三角形
                        for (int t = 0; t < 16; t += 3)
                        {
                            int i0 = MarchingCubesTables.TriTable[ci, t];
                            if (i0 < 0) break;
                            int i1 = MarchingCubesTables.TriTable[ci, t + 1];
                            int i2 = MarchingCubesTables.TriTable[ci, t + 2];

                            // 转换到 mesh 本地（中心原点）
                            Vector3 v0 = edgeVert[i0] - size * 0.5f;
                            Vector3 v1 = edgeVert[i1] - size * 0.5f;
                            Vector3 v2 = edgeVert[i2] - size * 0.5f;

                            int baseIdx = _vertBuf.Count;
                            _vertBuf.Add(v0);
                            _vertBuf.Add(v1);
                            _vertBuf.Add(v2);
                            // 顶点法线 = SDF 梯度（平滑着色）
                            _normalBuf.Add(SdfGradient(v0 + size * 0.5f));
                            _normalBuf.Add(SdfGradient(v1 + size * 0.5f));
                            _normalBuf.Add(SdfGradient(v2 + size * 0.5f));
                            // 默认干沙（wetness=0）
                            _colorBuf.Add(Color.black);
                            _colorBuf.Add(Color.black);
                            _colorBuf.Add(Color.black);
                            // 翻转绕序（修复法线朝内问题）
                            _triBuf.Add(baseIdx);
                            _triBuf.Add(baseIdx + 2);
                            _triBuf.Add(baseIdx + 1);
                        }
                    }
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertBuf);
            _mesh.SetColors(_colorBuf);
            _mesh.SetTriangles(_triBuf, 0);
            _mesh.SetNormals(_normalBuf);
            _mesh.RecalculateBounds();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
