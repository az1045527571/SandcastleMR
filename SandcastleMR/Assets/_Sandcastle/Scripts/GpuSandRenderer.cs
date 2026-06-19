using UnityEngine;

namespace Sandcastle
{
    // 与 compute 端 Vert 一一对应 (32 bytes: float4 pos + float4 nw)
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Vert
    {
        public Vector4 pos;   // xyz=世界坐标, w=pad
        public Vector4 nw;    // xyz=法线, w=湿度
    }

    /// <summary>
    /// GPU 沙子渲染器（阶段一最小验证）。
    /// 数据上 GPU + EvaluateBase kernel + MarchingCubes kernel + DrawProceduralIndirect。
    /// 只做静态沙层显示，不接写操作、不接碰撞（后续阶段）。
    /// 与 CPU SdfVolume 路径并存做对照，按 toggleKey 切换显示。
    /// 参考 docs/GPU_SAND_PHASE1.md。
    /// </summary>
    [RequireComponent(typeof(SdfVolume))]
    public class GpuSandRenderer : MonoBehaviour
    {
        [Tooltip("MC compute shader")]
        public ComputeShader compute;
        [Tooltip("GPU 沙子材质 Sandcastle/SandGPU")]
        public Material material;
        [Tooltip("按此键切换 GPU/CPU 路径显示（对照用）。G 留给样条模式，这里用 F2。")]
        public KeyCode toggleKey = KeyCode.F2;
        [Tooltip("是否一开始就用 GPU 路径")]
        public bool useGpu = true;
        [Tooltip("顶点容量系数（cube 数 × 此值）")]
        public float vertCapacityFactor = 5f;

        private SdfVolume _vol;
        private int _resX, _resY, _resZ, _nx, _ny, _nz, _voxCount, _cubeCount;

        private ComputeBuffer _sdfBaseBuf, _erosionBuf, _wetnessBuf;
        private ComputeBuffer _vertBuf, _counterBuf;
        private ComputeBuffer _edgeTable, _triTable, _edgeVertexIndex, _vertexOffset;
        private ComputeBuffer _pieceBuf, _splinePtsBuf;
        private int _kEvalBase, _kMC;
        private bool _dirty = true;
        private bool _baseDirtyGpu = true;  // 需重跑 GPU EvaluateBase(piece 增删)
        private int _vertCount = 0;
        private Bounds _bounds;
        private MeshRenderer _cpuRenderer;

        // 方案 B: GPU 顶点回读重建 collider mesh，保证碰撞与 GPU 显示一致
        public bool updateCollider = true;
        private MeshCollider _meshCollider;
        private Mesh _colliderMesh;
        private bool _colliderDirty = true;  // 几何变了才重建 collider

        // Piece struct: float4x4(64) + float4(16) + float4(16) = 96 bytes
        private const int PIECE_STRIDE = 96;
        private const int MAX_SPLINE_PTS = 4096;

        // Vert: float4 pos + float4 nw = 8 floats = 32 bytes (全 float4 对齐避免 padding 坑)
        private const int VERT_STRIDE = 32;

        void Start()
        {
            _vol = GetComponent<SdfVolume>();
            _cpuRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();
            if (_meshCollider == null) _meshCollider = gameObject.AddComponent<MeshCollider>();
            _colliderMesh = new Mesh();
            _colliderMesh.name = "GpuSandCollider";
            _colliderMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            if (compute == null) compute = Resources.Load<ComputeShader>("SandMarchingCubes");
            if (material == null)
            {
                var sh = Shader.Find("Sandcastle/SandGPU");
                if (sh != null) material = new Material(sh);
            }
            if (compute == null || material == null)
            {
                Debug.LogError("[GpuSand] 缺少 compute 或 material，禁用 GPU 路径");
                enabled = false;
                return;
            }

            _resX = _vol.resolutionX; _resY = _vol.resolutionY; _resZ = _vol.resolutionZ;
            _nx = _resX + 1; _ny = _resY + 1; _nz = _resZ + 1;
            _voxCount = _nx * _ny * _nz;
            _cubeCount = _resX * _resY * _resZ;

            AllocBuffers();
            UploadTables();
            SetComputeParams();
            ApplyRouting();
        }

        void AllocBuffers()
        {
            _sdfBaseBuf = new ComputeBuffer(_voxCount, sizeof(float));
            _erosionBuf = new ComputeBuffer(_voxCount, sizeof(float));
            _wetnessBuf = new ComputeBuffer(_voxCount, sizeof(float));
            // 初始 erosion/wetness = 0
            _erosionBuf.SetData(new float[_voxCount]);
            _wetnessBuf.SetData(new float[_voxCount]);

            int cap = Mathf.CeilToInt(_cubeCount * vertCapacityFactor);
            _vertBuf = new ComputeBuffer(cap, VERT_STRIDE);  // 普通 structured buffer, 手动原子计数
            _counterBuf = new ComputeBuffer(1, sizeof(uint));
            _counterBuf.SetData(new uint[] { 0 });

            // Piece / spline buffer：必须在 SetComputeParams 绑定前分配，否则 SetBuffer(null) 抛 ArgumentNullException
            _pieceBuf = new ComputeBuffer(64, PIECE_STRIDE);                 // 最多 64 个 piece
            _splinePtsBuf = new ComputeBuffer(MAX_SPLINE_PTS, sizeof(float) * 2); // Vector2 控制点

            _kEvalBase = compute.FindKernel("EvaluateBase");
            _kMC = compute.FindKernel("MarchingCubes");

            // 包围盒（世界）：体积尺寸，中心在物体位置
            _bounds = new Bounds(transform.position, _vol.size * 1.5f);
        }

        void UploadTables()
        {
            // EdgeTable[256]
            _edgeTable = new ComputeBuffer(256, sizeof(int));
            _edgeTable.SetData(MarchingCubesTables.EdgeTable);

            // TriTable[256,16] -> 展平 4096
            int[] tri = new int[256 * 16];
            for (int i = 0; i < 256; i++)
                for (int j = 0; j < 16; j++)
                    tri[i * 16 + j] = MarchingCubesTables.TriTable[i, j];
            _triTable = new ComputeBuffer(256 * 16, sizeof(int));
            _triTable.SetData(tri);

            // EdgeVertexIndex[12,2] -> 24
            int[] evi = new int[24];
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 2; j++)
                    evi[i * 2 + j] = MarchingCubesTables.EdgeVertexIndex[i, j];
            _edgeVertexIndex = new ComputeBuffer(24, sizeof(int));
            _edgeVertexIndex.SetData(evi);

            // VertexOffset[8,3] -> 24
            int[] vo = new int[24];
            for (int i = 0; i < 8; i++)
                for (int j = 0; j < 3; j++)
                    vo[i * 3 + j] = MarchingCubesTables.VertexOffset[i, j];
            _vertexOffset = new ComputeBuffer(24, sizeof(int));
            _vertexOffset.SetData(vo);
        }

        void SetComputeParams()
        {
            foreach (int k in new[] { _kEvalBase, _kMC })
            {
                compute.SetInt("_ResX", _resX);
                compute.SetInt("_ResY", _resY);
                compute.SetInt("_ResZ", _resZ);
                compute.SetVector("_Size", _vol.size);
                compute.SetFloat("_SandThickness", _vol.sandLayerThickness);
                compute.SetFloat("_SandInset", _vol.sandInset);
                compute.SetFloat("_IsoLevel", _vol.isoLevel);
            }
            // EvaluateBase 绑定
            compute.SetBuffer(_kEvalBase, "_SdfBaseBuf", _sdfBaseBuf);
            compute.SetBuffer(_kEvalBase, "_Pieces", _pieceBuf);
            compute.SetBuffer(_kEvalBase, "_SplinePts", _splinePtsBuf);
            // MarchingCubes 绑定
            compute.SetBuffer(_kMC, "_SdfBaseBuf", _sdfBaseBuf);
            compute.SetBuffer(_kMC, "_ErosionBuf", _erosionBuf);
            compute.SetBuffer(_kMC, "_WetnessBuf", _wetnessBuf);
            compute.SetBuffer(_kMC, "_VertBuf", _vertBuf);
            compute.SetBuffer(_kMC, "_Counter", _counterBuf);
            compute.SetBuffer(_kMC, "_EdgeTable", _edgeTable);
            compute.SetBuffer(_kMC, "_TriTable", _triTable);
            compute.SetBuffer(_kMC, "_EdgeVertexIndex", _edgeVertexIndex);
            compute.SetBuffer(_kMC, "_VertexOffset", _vertexOffset);
        }

        // 收集 SdfVolume 的 piece 参数上传 GPU
        int UploadPieces()
        {
            var pieces = _vol.Pieces;
            int n = 0;
            var pdata = new System.Collections.Generic.List<float>();   // 每 piece 24 floats (96B)
            var spts = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < pieces.Count && n < 64; i++)
            {
                var p = pieces[i];
                if (p == null) continue;
                Matrix4x4 w2l = p.transform.worldToLocalMatrix;
                int type;
                Vector4 p0, p1;
                switch (p.shape)
                {
                    case SdfPiece.ShapeType.Box:
                        type = 1;
                        float hb = p.radius; // CPU 版 halfSize = radius
                        p0 = new Vector4(hb, hb, hb, 0);
                        p1 = new Vector4(0, 0, 0, type);
                        break;
                    case SdfPiece.ShapeType.Spline:
                        type = 2;
                        int start = spts.Count;
                        if (p.splinePoints != null)
                            foreach (var sp in p.splinePoints) spts.Add(new Vector2(sp.x, sp.z));
                        int cnt = p.splinePoints != null ? p.splinePoints.Count : 0;
                        p0 = new Vector4(start, cnt, p.splineRadius, p.splineTopY);
                        p1 = new Vector4(p.splineBottomY, 0, 0, type);
                        break;
                    default: // Sphere (bakedmesh 也当球处理, 跳过烘焙)
                        type = 0;
                        Vector3 c = p.transform.position;
                        float r = p.radius * p.transform.lossyScale.x;
                        p0 = new Vector4(c.x, c.y, c.z, r);
                        p1 = new Vector4(0, 0, 0, type);
                        break;
                }
                // worldToLocal 矩阵 16 floats (球/spline 不用但占位)
                for (int r = 0; r < 4; r++)
                    for (int col = 0; col < 4; col++)
                        pdata.Add(w2l[r, col]);
                pdata.Add(p0.x); pdata.Add(p0.y); pdata.Add(p0.z); pdata.Add(p0.w);
                pdata.Add(p1.x); pdata.Add(p1.y); pdata.Add(p1.z); pdata.Add(p1.w);
                n++;
            }
            if (n > 0)
            {
                _pieceBuf.SetData(pdata.ToArray());
                if (spts.Count > 0) _splinePtsBuf.SetData(spts.ToArray());
            }
            return n;
        }

        void Rebuild()
        {
            compute.SetMatrix("_SandL2W", transform.localToWorldMatrix);

            // base(含 piece) 仅在 piece 增删时重算
            if (_baseDirtyGpu)
            {
                int pcount = UploadPieces();
                compute.SetInt("_PieceCount", pcount);
                compute.SetFloat("_SmoothK", _vol.smoothK);
                compute.Dispatch(_kEvalBase,
                    Mathf.CeilToInt(_nx / 4f), Mathf.CeilToInt(_ny / 4f), Mathf.CeilToInt(_nz / 4f));
                _baseDirtyGpu = false;
            }

            // erosion 从 CPU 上传（铲沙/侵蚀改的，便宜）; wetness 同理
            float[] ero = _vol.GetErosionData();
            if (ero != null && ero.Length == _voxCount) _erosionBuf.SetData(ero);

            // MarchingCubes
            _counterBuf.SetData(new uint[] { 0 });
            compute.Dispatch(_kMC,
                Mathf.CeilToInt(_resX / 4f), Mathf.CeilToInt(_resY / 4f), Mathf.CeilToInt(_resZ / 4f));

            uint[] cnt = new uint[1];
            _counterBuf.GetData(cnt);
            _vertCount = (int)cnt[0];

            // 方案 B：回读 GPU 顶点重建 collider mesh，让碰撞跟 GPU 显示一致
            if (updateCollider) RebuildCollider();

            _dirty = false;
        }

        // 回读 _vertBuf 前 _vertCount 个顶点，转回本地坐标建 collider mesh。
        // GPU 顶点是世界坐标（_SandL2W 变换过），collider 在本 transform 下，须 worldToLocal 转回。
        private Vert[] _readback;          // 复用避免每帧 GC
        private Vector3[] _colVerts;
        private int[] _colTris;
        void RebuildCollider()
        {
            if (_meshCollider == null) return;
            if (_vertCount <= 0 || _vertCount % 3 != 0)
            {
                _colliderMesh.Clear();
                _meshCollider.sharedMesh = null;
                return;
            }

            if (_readback == null || _readback.Length < _vertCount)
            {
                _readback = new Vert[Mathf.NextPowerOfTwo(_vertCount)];
                _colVerts = new Vector3[_readback.Length];
                _colTris = new int[_readback.Length];
            }
            // 同步回读（只在几何变动时跑，不是每帧）
            _vertBuf.GetData(_readback, 0, 0, _vertCount);

            Matrix4x4 w2l = transform.worldToLocalMatrix;
            for (int i = 0; i < _vertCount; i++)
            {
                Vector4 wp = _readback[i].pos;   // 世界坐标
                _colVerts[i] = w2l.MultiplyPoint3x4(new Vector3(wp.x, wp.y, wp.z));
                _colTris[i] = i;
            }

            _colliderMesh.Clear();
            _colliderMesh.SetVertices(_colVerts, 0, _vertCount);
            _colliderMesh.SetTriangles(_colTris, 0, _vertCount, 0, false);
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _colliderMesh;
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                useGpu = !useGpu;
                ApplyRouting();
            }

            if (!useGpu) return;

            if (_dirty) Rebuild();

            if (_vertCount <= 0) return;
            material.SetBuffer("_VertBuf", _vertBuf);
            Shader.SetGlobalBuffer("_VertBuf", _vertBuf); // 绕过 SRP Batcher 对 material buffer 的坑
            Graphics.DrawProcedural(material, _bounds, MeshTopology.Triangles, _vertCount, 1,
                null, null, UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
        }

        // 切换 GPU/CPU 路径：GPU 开时隐藏 CPU mesh renderer
        void ApplyRouting()
        {
            if (_cpuRenderer != null) _cpuRenderer.enabled = !useGpu;
            if (useGpu)
            {
                // 切回 GPU：重建一次，恢复 GPU 顶点 + collider
                _dirty = true;
            }
            else
            {
                // 切到 CPU：交还 collider 给 SdfVolume，强制它重算一次 CPU mesh
                _vol.RebuildMesh();
            }
        }

        /// <summary>外部标记需要重建（写操作后调用，阶段二接入）。</summary>
        public void MarkDirty() => _dirty = true;
        /// <summary>标记 base 需重算（piece 增删后）。</summary>
        public void MarkBaseDirty() { _baseDirtyGpu = true; _dirty = true; }

        void OnDestroy()
        {
            _sdfBaseBuf?.Release();
            _erosionBuf?.Release();
            _wetnessBuf?.Release();
            _vertBuf?.Release();
            _counterBuf?.Release();
            _edgeTable?.Release();
            _triTable?.Release();
            _edgeVertexIndex?.Release();
            _vertexOffset?.Release();
            _pieceBuf?.Release();
            _splinePtsBuf?.Release();
            if (_colliderMesh != null) Destroy(_colliderMesh);
        }
    }
}
