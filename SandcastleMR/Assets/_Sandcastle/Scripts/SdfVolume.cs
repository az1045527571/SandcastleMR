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

        // 体素数据
        private float[] _sdf;
        private int Nx => resolutionX + 1;
        private int Ny => resolutionY + 1;
        private int Nz => resolutionZ + 1;

        // 注册的 SDF 形状
        private readonly List<SdfPiece> _pieces = new List<SdfPiece>();

        private Mesh _mesh;
        private MeshFilter _meshFilter;

        // 输出 buffer（避免每次 alloc）
        private List<Vector3> _vertBuf = new List<Vector3>(8192);
        private List<int> _triBuf = new List<int>(16384);

        void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _mesh = new Mesh();
            _mesh.name = "SdfMesh";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _meshFilter.sharedMesh = _mesh;

            _sdf = new float[Nx * Ny * Nz];
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
                        // 体素中心的本地坐标（角落原点）
                        Vector3 localPos = new Vector3(x * dx, y * dy, z * dz);
                        Vector3 worldPos = LocalToWorld(localPos);

                        float d = float.PositiveInfinity;
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

        /// <summary>Marching Cubes 提取 mesh（CPU 版）。</summary>
        void ExtractMesh()
        {
            _vertBuf.Clear();
            _triBuf.Clear();

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
                            _triBuf.Add(baseIdx);
                            _triBuf.Add(baseIdx + 1);
                            _triBuf.Add(baseIdx + 2);
                        }
                    }
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertBuf);
            _mesh.SetTriangles(_triBuf, 0);
            _mesh.RecalculateNormals();
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
