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
        [Tooltip("体积尺寸（米）。还原换方案前 5×1.5×5")]
        public Vector3 size = new Vector3(5f, 1.5f, 5f);
        [Tooltip("各轴体素数。还原 96×32×96")]
        public int resolutionX = 96;
        public int resolutionY = 32;
        public int resolutionZ = 96;

        [Header("初始沙层")]
        [Tooltip("初始实心沙层厚度（米）。从体积底部算起。体积底世界-0.25, 沙面-0.10 → 0.15m")]
        public float sandLayerThickness = 0.15f;
        [Tooltip("沙层 XZ 向内缩多少（米），让 MC 能封住侧壁")]
        public float sandInset = 0.0f;

        [Header("Smooth Union")]
        [Tooltip("smooth min 平滑系数。越大融合越柔和但会损失细节")]
        public float smoothK = 0.2f;

        [Header("ISO 表面")]
        [Tooltip("等值面阈值。0 = SDF 表面")]
        public float isoLevel = 0f;

        [Header("侵蚀")]
        [Tooltip("湿沙抗侵蚀强度。0=湿沙照常被冲, 1=完全湿沙不被侵蚀")]
        [Range(0f, 1f)]
        public float wetResistance = 0.85f;

        // 本次侵蚀中刚刚被冲成空气的体素世界坐标（供粒子特效用，每次 SurfaceErode 清空重填）
        public readonly List<Vector3> LastErodedPoints = new List<Vector3>(64);
        private const int MaxErodedPoints = 48;

        // 体素数据
        private float[] _sdf;
        private float[] _sdfBase;
        private float[] _erosion;
        private float[] _wetness;  // 每个体素的湿度 0~1
        private bool _baseDirty = true;
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
            _sdfBase = new float[Nx * Ny * Nz];
            _erosion = new float[Nx * Ny * Nz];
            _wetness = new float[Nx * Ny * Nz];
        }

        void Start()
        {
            // 初始建一次：填出有厚度的沙层
            RebuildMesh();
        }

        public void Register(SdfPiece p)
        {
            if (!_pieces.Contains(p)) _pieces.Add(p);
            _baseDirty = true;
            RebuildMesh();
        }

        public void Unregister(SdfPiece p)
        {
            if (_pieces.Remove(p))
            {
                _baseDirty = true;
                RebuildMesh();
            }
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

        /// <summary>沙层表面的实际世界 Y（跟随根物体偏移）。水面/潮汐应以此为基准。</summary>
        public float SandSurfaceWorldY => LocalToWorld(new Vector3(0f, sandLayerThickness, 0f)).y;

        /// <summary>获取当前 SDF 数据数组引用（只读）</summary>
        public float[] GetSdfData() => _sdf;

        /// <summary>将指定体素擦除（设侵蚀值让其变为空气）</summary>
        public void EraseVoxels(System.Collections.Generic.List<int> indices)
        {
            foreach (int idx in indices)
            {
                // 强制设侵蚀值让该体素变正（空气）
                _erosion[idx] = Mathf.Max(_erosion[idx], -_sdfBase[idx] + 0.01f);
            }
        }

        /// <summary>
        /// 铲子 Box 笔刷：在世界坐标 center 为中心、halfExtents 为半边长的盒子区域内修改 erosion。
        /// dig=true 挖（erosion 加正，嵌深 depth 米，表面凹下）；dig=false 填（erosion 加负，凸起）。
        /// box 可随 rotationY 朝向旋转。返回实际受影响的实心体素数（供守恒估算）。
        /// 调用后需 RebuildMesh()。
        /// </summary>
        public int BoxBrush(Vector3 worldCenter, Vector3 halfExtents, float rotationY, bool dig, float amount)
        {
            float dx = size.x / resolutionX;
            float dy = size.y / resolutionY;
            float dz = size.z / resolutionZ;
            float cy = Mathf.Cos(-rotationY * Mathf.Deg2Rad);
            float sy = Mathf.Sin(-rotationY * Mathf.Deg2Rad);
            int affected = 0;

            // 包围盒对应的体素索引范围（用最大半径保守估算，避免遗漏旋转后的角）
            float reach = new Vector2(halfExtents.x, halfExtents.z).magnitude;
            for (int z = 0; z < Nz; z++)
            {
                for (int y = 0; y < Ny; y++)
                {
                    for (int x = 0; x < Nx; x++)
                    {
                        Vector3 wp = LocalToWorld(new Vector3(x * dx, y * dy, z * dz));
                        Vector3 rel = wp - worldCenter;
                        // 反旋转到 box 本地（绕 Y）
                        float lx = rel.x * cy - rel.z * sy;
                        float lz = rel.x * sy + rel.z * cy;
                        float ly = rel.y;
                        if (Mathf.Abs(lx) > halfExtents.x) continue;
                        if (Mathf.Abs(ly) > halfExtents.y) continue;
                        if (Mathf.Abs(lz) > halfExtents.z) continue;

                        int idx = Index(x, y, z);
                        bool wasSolid = (_sdfBase[idx] + _erosion[idx]) < 0f;
                        if (dig)
                        {
                            // 挖：erosion 加正，让该体素变空气
                            _erosion[idx] += amount;
                            if (wasSolid) affected++;
                        }
                        else
                        {
                            // 填：erosion 加负，让该体素变实心（不低于 base 原始实心度）
                            _erosion[idx] -= amount;
                            bool nowSolid = (_sdfBase[idx] + _erosion[idx]) < 0f;
                            if (!wasSolid && nowSolid) affected++;
                        }
                    }
                }
            }
            return affected;
        }

        // 连通域检测复用缓冲（避免每帧 alloc）
        private bool[] _supported;
        private readonly Queue<int> _floodQueue = new Queue<int>(4096);

        /// <summary>
        /// 连通域检测：以沙箱最底几层实心体素作为“地基”种子，6 邻域 flood fill 向上扩散。
        /// 凡是没和地基连通的实心体素（无支撑）立即擦除，并返回其世界坐标供掉渣粒子使用。
        /// 返回被移除的体素数。调用后需 RebuildMesh()。
        /// </summary>
        public int RemoveUnsupported(System.Collections.Generic.List<Vector3> removedPointsOut, int maxPoints = 48)
        {
            int total = _sdf.Length;
            if (_supported == null || _supported.Length != total)
                _supported = new bool[total];
            else
                System.Array.Clear(_supported, 0, total);
            _floodQueue.Clear();

            float dx = size.x / resolutionX;
            float dy = size.y / resolutionY;
            float dz = size.z / resolutionZ;

            // 地基种子：沙箱最底两层体素（y<=1）中的实心体素
            for (int z = 0; z < Nz; z++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int x = 0; x < Nx; x++)
                    {
                        int idx = Index(x, y, z);
                        if (_sdf[idx] >= 0f) continue;
                        _supported[idx] = true;
                        _floodQueue.Enqueue(idx);
                    }
                }
            }

            // BFS（6 邻）
            while (_floodQueue.Count > 0)
            {
                int idx = _floodQueue.Dequeue();
                int x = idx % Nx;
                int y = (idx / Nx) % Ny;
                int z = idx / (Nx * Ny);
                TrySpread(x - 1, y, z);
                TrySpread(x + 1, y, z);
                TrySpread(x, y - 1, z);
                TrySpread(x, y + 1, z);
                TrySpread(x, y, z - 1);
                TrySpread(x, y, z + 1);
            }

            // 擦除所有实心但无支撑的体素
            int removed = 0;
            if (removedPointsOut != null) removedPointsOut.Clear();
            for (int z = 0; z < Nz; z++)
            {
                for (int y = 0; y < Ny; y++)
                {
                    for (int x = 0; x < Nx; x++)
                    {
                        int idx = Index(x, y, z);
                        if (_sdf[idx] >= 0f || _supported[idx]) continue;
                        _erosion[idx] = Mathf.Max(_erosion[idx], -_sdfBase[idx] + 0.01f);
                        removed++;
                        if (removedPointsOut != null && removedPointsOut.Count < maxPoints && (x + y + z) % 3 == 0)
                            removedPointsOut.Add(LocalToWorld(new Vector3(x * dx, y * dy, z * dz)));
                    }
                }
            }
            return removed;
        }

        void TrySpread(int x, int y, int z)
        {
            if (x < 0 || x >= Nx || y < 0 || y >= Ny || z < 0 || z >= Nz) return;
            int idx = Index(x, y, z);
            if (_supported[idx] || _sdf[idx] >= 0f) return;
            _supported[idx] = true;
            _floodQueue.Enqueue(idx);
        }

        /// <summary>
        /// 重新计算整个 SDF 并提取 mesh。
        /// </summary>
        /// <summary>
        /// 侵蚀水位以下一定带幅内的体素。
        /// 增加那些体素的侵蚀场。调用后需手动 RebuildMesh()。
        /// </summary>
        /// <summary>
        /// 均匀表面侵蚀（形态学腐蚀）：在侵蚀带内给所有体素的 SDF 整体加上一个偏移量。
        /// SDF 整体 +amount 等价于表面沿法线向内后退 amount 米，于是沙堡逐层“化开”，
        /// 凸出的尖角比平面后退得快，整体变小变圆。
        /// 湿沙（_wetness）会按 wetResistance 减缓侵蚀。
        /// 只作用于水面以下一定带幅内的体素。调用后需 RebuildMesh()。
        /// </summary>
        public void SurfaceErode(float waterY, float amount, float bandHeight)
        {
            float dy = size.y / resolutionY;
            LastErodedPoints.Clear();

            for (int z = 0; z < Nz; z++)
            {
                for (int y = 0; y < Ny; y++)
                {
                    float localY = y * dy;
                    float worldY = LocalToWorld(new Vector3(0, localY, 0)).y;
                    // 只侵蚀海面以下 5cm 到海面以上 bandHeight 范围内的体素
                    if (worldY > waterY + bandHeight) continue;
                    if (worldY < waterY - 0.05f) continue;
                    for (int x = 0; x < Nx; x++)
                    {
                        int idx = Index(x, y, z);
                        // 被海水泡到 = 变湿（只是描述表面被水打湿，不代表玩家浇水的护城河效果）
                        if (worldY <= waterY)
                            _wetness[idx] = Mathf.Max(_wetness[idx], 0.5f);

                        float curr = _sdfBase[idx] + _erosion[idx];
                        if (curr > 0f) continue;        // 空气不侵蚀

                        // 湿沙抗侵蚀：湿度越高侵蚀量越少
                        float resist = 1f - _wetness[idx] * wetResistance;
                        if (resist <= 0f) continue;
                        float applied = amount * resist;
                        _erosion[idx] += applied;

                        // 表面体素被冲成空气的瞬间，记录为碎屑粒子的生成点
                        if (curr > -0.02f && (_sdfBase[idx] + _erosion[idx]) > 0f
                            && LastErodedPoints.Count < MaxErodedPoints)
                        {
                            Vector3 lp = new Vector3(x * (size.x / resolutionX), localY, z * (size.z / resolutionZ));
                            LastErodedPoints.Add(LocalToWorld(lp));
                        }
                    }
                }
            }
        }

        /// <summary>玩家浇水：在世界坐标 center 周围 radius 米内增加体素湿度。
        /// 湿沙会按 wetResistance 抵抗海浪侵蚀，打造护城河/加固效果。
        /// </summary>
        public void WetVolume(Vector3 worldCenter, float radius, float amount)
        {
            float dx = size.x / resolutionX;
            float dy = size.y / resolutionY;
            float dz = size.z / resolutionZ;
            float r2 = radius * radius;
            for (int z = 0; z < Nz; z++)
            {
                for (int y = 0; y < Ny; y++)
                {
                    for (int x = 0; x < Nx; x++)
                    {
                        Vector3 wp = LocalToWorld(new Vector3(x * dx, y * dy, z * dz));
                        float d2 = (wp - worldCenter).sqrMagnitude;
                        if (d2 > r2) continue;
                        int idx = Index(x, y, z);
                        // 只给实体体素加湿
                        if (_sdfBase[idx] + _erosion[idx] > 0.02f) continue;
                        float falloff = 1f - Mathf.Sqrt(d2) / radius;
                        _wetness[idx] = Mathf.Clamp01(_wetness[idx] + amount * falloff);
                    }
                }
            }
        }

        /// <summary>湿度是否需要重建 mesh 以显示湿沙色。仅在玩家浇水后调。</summary>
        public void RefreshWetnessVisual() => RebuildMesh();

        /// <summary>每帧调一次让湿度蒸发。应在 Update 里调。</summary>
        public void DecayWetness(float decayPerSecond)
        {
            float decay = decayPerSecond * Time.deltaTime;
            for (int i = 0; i < _wetness.Length; i++)
            {
                if (_wetness[i] > 0f)
                    _wetness[i] = Mathf.Max(0f, _wetness[i] - decay);
            }
        }

        public void RebuildMesh()
        {
            if (_baseDirty)
            {
                EvaluateBase();
                _baseDirty = false;
            }
            // 最终 SDF = base + erosion
            for (int i = 0; i < _sdf.Length; i++)
                _sdf[i] = _sdfBase[i] + _erosion[i];
            ExtractMesh();
        }

        /// <summary>重新计算基础 SDF（地形 + 所有 piece）。耗时操作，仅在 piece 增删时调用。</summary>
        void EvaluateBase()
        {
            float dx = size.x / resolutionX;
            float dy = size.y / resolutionY;
            float dz = size.z / resolutionZ;

            // 初始沙层 = 一个有限厚度的 box（体积局部坐标，角落原点）
            // Y: 0 ~ sandLayerThickness；XZ: inset ~ size-inset
            Vector3 boxCenter = new Vector3(size.x * 0.5f, sandLayerThickness * 0.5f, size.z * 0.5f);
            Vector3 boxHalf = new Vector3(size.x * 0.5f - sandInset,
                                          sandLayerThickness * 0.5f,
                                          size.z * 0.5f - sandInset);

            for (int z = 0; z < Nz; z++)
            {
                for (int y = 0; y < Ny; y++)
                {
                    for (int x = 0; x < Nx; x++)
                    {
                        Vector3 localPos = new Vector3(x * dx, y * dy, z * dz);
                        Vector3 worldPos = LocalToWorld(localPos);

                        // 基础沙层 box SDF
                        float d = SdfBoxLocal(localPos, boxCenter, boxHalf);

                        // 叠加所有 piece（世界坐标采样）
                        for (int i = 0; i < _pieces.Count; i++)
                        {
                            float di = _pieces[i].SampleSdf(worldPos);
                            d = SmoothMin(d, di, smoothK);
                        }

                        // 体积最外一圈强制为空气，保证 MC 能封住边界面
                        bool atBorder = (x == 0 || x == Nx - 1 || y == 0 || y == Ny - 1 ||
                                         z == 0 || z == Nz - 1);
                        if (atBorder) d = Mathf.Max(d, 0.01f);
                        _sdfBase[Index(x, y, z)] = d;
                    }
                }
            }
        }

        /// <summary>体积局部坐标下的 box SDF。</summary>
        static float SdfBoxLocal(Vector3 p, Vector3 center, Vector3 half)
        {
            Vector3 q = new Vector3(
                Mathf.Abs(p.x - center.x) - half.x,
                Mathf.Abs(p.y - center.y) - half.y,
                Mathf.Abs(p.z - center.z) - half.z);
            float outside = new Vector3(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0), Mathf.Max(q.z, 0)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            return outside + inside;
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
        /// <summary>采样 cube (x,y,z) 中心 8 个角的平均湿度</summary>
        float SampleWetnessAtCube(int x, int y, int z)
        {
            float sum = 0;
            sum += _wetness[Index(x, y, z)];
            sum += _wetness[Index(Mathf.Min(x+1, resolutionX), y, z)];
            sum += _wetness[Index(x, Mathf.Min(y+1, resolutionY), z)];
            sum += _wetness[Index(x, y, Mathf.Min(z+1, resolutionZ))];
            return Mathf.Clamp01(sum * 0.25f);
        }

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
                            // 顶点湿度（采样当前 cube 中心的湿度）
                            float wet = SampleWetnessAtCube(x, y, z);
                            Color wetCol = new Color(wet, 0, 0, 0);
                            _colorBuf.Add(wetCol);
                            _colorBuf.Add(wetCol);
                            _colorBuf.Add(wetCol);
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
