using UnityEngine;

namespace Sandcastle
{
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
        [Tooltip("按此键切换 GPU/CPU 路径显示（对照用）")]
        public KeyCode toggleKey = KeyCode.G;
        [Tooltip("是否一开始就用 GPU 路径")]
        public bool useGpu = true;
        [Tooltip("顶点容量系数（cube 数 × 此值）")]
        public float vertCapacityFactor = 5f;

        private SdfVolume _vol;
        private int _resX, _resY, _resZ, _nx, _ny, _nz, _voxCount, _cubeCount;

        private ComputeBuffer _sdfBaseBuf, _erosionBuf, _wetnessBuf;
        private ComputeBuffer _vertBuf, _indirectArgs;
        private ComputeBuffer _edgeTable, _triTable, _edgeVertexIndex, _vertexOffset;
        private int _kEvalBase, _kMC;
        private bool _dirty = true;
        private bool _loggedOnce = false;
        private Bounds _bounds;
        private MeshRenderer _cpuRenderer;

        // Vert: float4 pos + float4 nw = 8 floats = 32 bytes (全 float4 对齐避免 padding 坑)
        private const int VERT_STRIDE = 32;

        void Start()
        {
            _vol = GetComponent<SdfVolume>();
            _cpuRenderer = GetComponent<MeshRenderer>();

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
            _vertBuf = new ComputeBuffer(cap, VERT_STRIDE, ComputeBufferType.Append);
            _indirectArgs = new ComputeBuffer(1, 4 * sizeof(uint), ComputeBufferType.IndirectArguments);
            // args: vertexCountPerInstance, instanceCount, startVertex, startInstance
            _indirectArgs.SetData(new uint[] { 0, 1, 0, 0 });

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
            // MarchingCubes 绑定
            compute.SetBuffer(_kMC, "_SdfBaseBuf", _sdfBaseBuf);
            compute.SetBuffer(_kMC, "_ErosionBuf", _erosionBuf);
            compute.SetBuffer(_kMC, "_WetnessBuf", _wetnessBuf);
            compute.SetBuffer(_kMC, "_VertBuf", _vertBuf);
            compute.SetBuffer(_kMC, "_EdgeTable", _edgeTable);
            compute.SetBuffer(_kMC, "_TriTable", _triTable);
            compute.SetBuffer(_kMC, "_EdgeVertexIndex", _edgeVertexIndex);
            compute.SetBuffer(_kMC, "_VertexOffset", _vertexOffset);
        }

        void Rebuild()
        {
            // 阶段一诊断: 直接上传 CPU 已验证的 SDF 场 (绕过 GPU EvaluateBase)
            // CPU _sdf = _sdfBase + _erosion, 已是最终场; erosion 置 0 避免重复叠加
            float[] cpuSdf = _vol.GetSdfData();
            bool uploaded = false;
            if (cpuSdf != null && cpuSdf.Length == _voxCount)
            {
                _sdfBaseBuf.SetData(cpuSdf);
                uploaded = true;
            }
            else
            {
                // 回退: GPU 算 base
                compute.Dispatch(_kEvalBase,
                    Mathf.CeilToInt(_nx / 4f), Mathf.CeilToInt(_ny / 4f), Mathf.CeilToInt(_nz / 4f));
            }

            // MarchingCubes: 重置计数器后 dispatch over cube 网格
            compute.SetMatrix("_SandL2W", transform.localToWorldMatrix);
            _vertBuf.SetCounterValue(0);
            compute.Dispatch(_kMC,
                Mathf.CeilToInt(_resX / 4f), Mathf.CeilToInt(_resY / 4f), Mathf.CeilToInt(_resZ / 4f));

            // 把 append 计数复制到 indirect args 的 vertexCountPerInstance
            ComputeBuffer.CopyCount(_vertBuf, _indirectArgs, 0);

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                // 回读 indirect args 看实际顶点数
                uint[] args = new uint[4];
                _indirectArgs.GetData(args);
                int cap = _vertBuf.count;
                // 统计上传的 SDF 场
                int neg = 0; float mn = 9999f, mx = -9999f;
                if (cpuSdf != null)
                {
                    for (int i = 0; i < cpuSdf.Length; i++)
                    {
                        float v = cpuSdf[i];
                        if (v < 0f) neg++;
                        if (v < mn) mn = v;
                        if (v > mx) mx = v;
                    }
                }
                Debug.Log($"[GpuSand] 上传CPU场={uploaded} 体素数={_voxCount} cube数={_cubeCount}\n"
                    + $"SDF场: 负值(实心)={neg} min={mn:F3} max={mx:F3}\n"
                    + $"顶点buffer容量={cap} 实际append顶点数={args[0]} instanceCount={args[1]}\n"
                    + $"VERT_STRIDE={VERT_STRIDE} size={_vol.size} 分辨率={_resX}x{_resY}x{_resZ}");

                // 回读前 6 个顶点的实际坐标 (本地中心原点), 看是否在 ±size/2 范围内
                int n = (int)args[0];
                if (n > 0)
                {
                    int sample = Mathf.Min(n, 6);
                    var verts = new Vector4[sample * 2]; // 每顶点 2 个 float4 (pos, nw)
                    _vertBuf.GetData(verts, 0, 0, sample * 2);
                    var sb = new System.Text.StringBuilder("[GpuSand] 前6顶点(本地坐标,应在±"
                        + (_vol.size * 0.5f) + "):\n");
                    for (int i = 0; i < sample; i++)
                    {
                        Vector4 p = verts[i * 2];
                        Vector4 nw = verts[i * 2 + 1];
                        sb.AppendLine($"  v{i} pos=({p.x:F2},{p.y:F2},{p.z:F2}) n=({nw.x:F2},{nw.y:F2},{nw.z:F2}) wet={nw.w:F2}");
                    }
                    Debug.Log(sb.ToString());
                }
            }

            _dirty = false;
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

            material.SetBuffer("_VertBuf", _vertBuf);
            Graphics.DrawProceduralIndirect(material, _bounds, MeshTopology.Triangles,
                _indirectArgs, 0, null, null, UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
        }

        // 切换 GPU/CPU 路径：GPU 开时隐藏 CPU mesh renderer
        void ApplyRouting()
        {
            if (_cpuRenderer != null) _cpuRenderer.enabled = !useGpu;
        }

        /// <summary>外部标记需要重建（写操作后调用，阶段二接入）。</summary>
        public void MarkDirty() => _dirty = true;

        void OnDestroy()
        {
            _sdfBaseBuf?.Release();
            _erosionBuf?.Release();
            _wetnessBuf?.Release();
            _vertBuf?.Release();
            _indirectArgs?.Release();
            _edgeTable?.Release();
            _triTable?.Release();
            _edgeVertexIndex?.Release();
            _vertexOffset?.Release();
        }
    }
}
