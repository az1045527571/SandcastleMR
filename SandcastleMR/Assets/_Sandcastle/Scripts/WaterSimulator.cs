using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// GPU 浅水高度场求解器(虚拟管道模型)。在 SdfVolume 的 XZ 网格上模拟水深 h。
    /// 地形底 = SdfVolume 实时沙面高度。水从高往低流, 封闭洼地积成独立池塘。
    /// 阶段一: 求解器 + 最简 unlit 蓝水面网格(验证流动/积水)。折射/焦散等美术后续。
    /// </summary>
    [RequireComponent(typeof(SdfVolume))]
    public class WaterSimulator : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("浅水 compute shader (Resources/ShallowWater)")]
        public ComputeShader compute;
        [Tooltip("水面材质(先用 unlit 半透明蓝)")]
        public Material waterMaterial;

        [Header("求解参数")]
        [Tooltip("每帧子步数(越多越稳, 越慢)")]
        [Range(1, 8)] public int subSteps = 4;
        [Tooltip("重力(调流速)")]
        public float gravity = 9.81f;
        [Tooltip("流量阻尼 0~1(每步乘, 抑制抖动)")]
        [Range(0.9f, 1f)] public float damping = 0.96f;
        [Tooltip("低于此水深视为干(米)")]
        public float minDepth = 0.002f;
        [Tooltip("死水带: 相邻总水位差小于此不驱动水流(米)。平滑地形下建议设为 0.0m 以求水面完全水平")]
        public float deadBand = 0.0f;

        [Header("潮汐")]
        [Tooltip("目标水位(相对沙箱底的本地Y, 米)。水会涨退逼近此位")]
        public float tideTargetLocalY = 0.3f;
        [Tooltip("逼近目标水位的速率")]
        public float tideFillRate = 0.5f;

        [Header("水沙联动")]
        [Tooltip("开启后，GPU 浅水高度场会按局部水深和流速侵蚀 SDF 沙体")]
        public bool erosionEnabled = true;
        [Tooltip("满强度水流每秒让 SDF 表面后退多少米")]
        public float erodePerSecond = 0.006f;
        [Tooltip("水面上方多少米内的表面参与冲刷")]
        public float erodeBandAboveWater = 0.08f;
        [Tooltip("水面下方多少米内的表面参与冲刷")]
        public float erodeBandBelowWater = 0.05f;
        [Tooltip("侵蚀/湿度回读与重建间隔。越小越连贯，越大越省")]
        public float erosionInterval = 0.25f;
        [Tooltip("低于此水深只渲染/流动，不侵蚀")]
        public float erosionMinDepth = 0.006f;
        [Tooltip("达到满静水侵蚀强度所需水深")]
        public float erosionDepthForFullStrength = 0.04f;
        [Tooltip("达到满水流冲刷强度所需近似流速(m/s)")]
        public float erosionFlowSpeedForFullStrength = 0.30f;
        [Tooltip("0=只看水深浸泡，1=只看流速冲刷")]
        [Range(0f, 1f)] public float flowErosionWeight = 0.65f;
        [Tooltip("被水浸泡后自动固化到的湿度上限。玩家浇水可继续加到 1")]
        [Range(0f, 1f)] public float waterWetnessTarget = 0.55f;
        [Tooltip("水浸泡每秒增加湿度")]
        public float waterWetnessPerSecond = 0.8f;
        [Tooltip("湿度自然蒸发速度(每秒)")]
        public float wetnessDecay = 0.04f;

        private SdfVolume _vol;
        private int _w, _h, _cellCount;
        private float _cellSize;
        private ComputeBuffer _terrainBuf, _depthBuf, _fluxBuf;
        private int _kFlux, _kApply, _kTide;
        private float _surfaceTimer;
        private float[] _oldTerrain;
        private float[] _tempDepth;
        private float[] _erosionDepth;
        private float[] _erosionFlux;
        private float _erosionTimer;
        private ErosionParticles _particles;

        // 水面网格
        private Mesh _waterMesh;
        private GameObject _waterGo;

        void Start()
        {
            _vol = GetComponent<SdfVolume>();
            if (compute == null) compute = Resources.Load<ComputeShader>("ShallowWater");
            if (compute == null) { Debug.LogError("[Water] 缺 ShallowWater compute"); enabled = false; return; }

            _w = _vol.GridNx;
            _h = _vol.GridNz;
            _cellCount = _w * _h;
            _cellSize = _vol.CellSizeX;

            _terrainBuf = new ComputeBuffer(_cellCount, sizeof(float));
            _depthBuf = new ComputeBuffer(_cellCount, sizeof(float));
            _fluxBuf = new ComputeBuffer(_cellCount * 4, sizeof(float));
            _depthBuf.SetData(new float[_cellCount]);
            _fluxBuf.SetData(new float[_cellCount * 4]);

            _kFlux = compute.FindKernel("UpdateFlux");
            _kApply = compute.FindKernel("ApplyFlux");
            _kTide = compute.FindKernel("ApplyTide");

            UploadTerrain();
            BuildWaterMesh();
        }

        /// <summary>从 SdfVolume 拿实时沙面高度 上传为地形底。</summary>
        void UploadTerrain()
        {
            float[] surf = _vol.GetSurfaceHeightField();   // 本地 Y, Nx*Nz
            if (_oldTerrain == null || _oldTerrain.Length != _cellCount)
            {
                _oldTerrain = new float[_cellCount];
                System.Array.Copy(surf, _oldTerrain, _cellCount);
                _terrainBuf.SetData(surf);
                return;
            }

            // 读取当前的 depth 数据 (从 GPU 回读)
            if (_tempDepth == null || _tempDepth.Length != _cellCount)
                _tempDepth = new float[_cellCount];
            _depthBuf.GetData(_tempDepth);

            // 根据地形变化调整深度，防止溢出到沙子上方
            bool changed = false;
            for (int i = 0; i < _cellCount; i++)
            {
                float oldT = _oldTerrain[i];
                float newT = surf[i];
                if (Mathf.Abs(newT - oldT) > 0.001f)
                {
                    float oldD = _tempDepth[i];
                    if (oldD <= 0f)
                    {
                        // 之前是干状态，保持干，防止凭空生水
                        _tempDepth[i] = 0f;
                    }
                    else
                    {
                        float oldTotal = oldT + oldD;
                        _tempDepth[i] = Mathf.Max(oldTotal - newT, 0f);
                    }
                    changed = true;
                }
            }

            if (changed)
            {
                _depthBuf.SetData(_tempDepth);
            }

            System.Array.Copy(surf, _oldTerrain, _cellCount);
            _terrainBuf.SetData(surf);
        }

        /// <summary>建 W×H 顶点网格平铺 XZ。顶点 Y 由 shader 读水深抬高。</summary>
        void BuildWaterMesh()
        {
            _waterMesh = new Mesh { name = "WaterSurface" };
            _waterMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            var verts = new Vector3[_cellCount];
            var uvs = new Vector2[_cellCount];
            float dx = _vol.CellSizeX, dz = _vol.CellSizeZ;
            // 顶点在 SDF 体本地系(原点在角), 交给 _waterGo 的 transform 跟 SdfVolume 同步
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    int i = x + z * _w;
                    verts[i] = new Vector3(x * dx, 0f, z * dz);
                    uvs[i] = new Vector2((float)x / (_w - 1), (float)z / (_h - 1));
                }

            var tris = new int[(_w - 1) * (_h - 1) * 6];
            int t = 0;
            for (int z = 0; z < _h - 1; z++)
                for (int x = 0; x < _w - 1; x++)
                {
                    int i = x + z * _w;
                    tris[t++] = i; tris[t++] = i + _w; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + _w; tris[t++] = i + _w + 1;
                }

            _waterMesh.vertices = verts;
            _waterMesh.uv = uvs;
            _waterMesh.triangles = tris;
            _waterMesh.bounds = new Bounds(new Vector3(_w * dx * 0.5f, 0, _h * dz * 0.5f), new Vector3(_w * dx, 4f, _h * dz));

            _waterGo = new GameObject("WaterSurfaceMesh");
            // 跟 SDF 体同一本地系: 原点移到角(减 size*0.5)
            _waterGo.transform.SetParent(_vol.transform, false);
            _waterGo.transform.localPosition = -_vol.size * 0.5f;
            _waterGo.AddComponent<MeshFilter>().sharedMesh = _waterMesh;
            var mr = _waterGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = waterMaterial != null ? waterMaterial : MakeFallbackMat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Material MakeFallbackMat()
        {
            // 顶点位移水面 shader: 读 _DepthBuf/_TerrainBuf 抬高顶点, 干格隐藏。
            var sh = Shader.Find("Sandcastle/ShallowWaterSurface");
            if (sh == null) { Debug.LogError("[Water] 缺 ShallowWaterSurface shader"); sh = Shader.Find("Universal Render Pipeline/Unlit"); }
            return new Material(sh);
        }

        void Update()
        {
            if (_vol == null) return;
            float dt = Time.deltaTime / subSteps;

            // 沙面可能改了(放/删/铲挖/塔陷): 仅在沙面变化时更新地形数据，防止浮点微调阻碍水面上升
            if (_vol.ConsumeTerrainChanged())
            {
                UploadTerrain();
            }

            // 获取统一的潮汐目标水位
            var tide = GetComponent<TideController>();
            if (tide != null)
            {
                tideTargetLocalY = tide.CurrentTideLocalY;
            }

            compute.SetInt("_W", _w);
            compute.SetInt("_H", _h);
            compute.SetFloat("_CellSize", _cellSize);
            compute.SetFloat("_Dt", dt);
            compute.SetFloat("_Gravity", gravity);
            compute.SetFloat("_Damping", damping);
            compute.SetFloat("_MinDepth", minDepth);
            compute.SetFloat("_DeadBand", deadBand);
            compute.SetFloat("_TideTargetLevel", tideTargetLocalY);
            compute.SetFloat("_TideFillRate", tideFillRate);

            int gx = (_w + 7) / 8, gz = (_h + 7) / 8;
            // 潮汐: 每帧一次(不进子步循环, 否则与 flux 每帧打架 N 次 → 震荡)
            BindAll(_kTide);
            compute.SetFloat("_Dt", Time.deltaTime);
            compute.Dispatch(_kTide, gx, gz, 1);
            // 水流求解: 子步提高稳定性
            compute.SetFloat("_Dt", dt);
            for (int s = 0; s < subSteps; s++)
            {
                BindAll(_kFlux); compute.Dispatch(_kFlux, gx, gz, 1);
                BindAll(_kApply); compute.Dispatch(_kApply, gx, gz, 1);
            }

            // 水面材质读 buffer 抬高顶点
            if (_waterGo != null)
            {
                var mat = _waterGo.GetComponent<MeshRenderer>().sharedMaterial;
                mat.SetBuffer("_DepthBuf", _depthBuf);
                mat.SetBuffer("_TerrainBuf", _terrainBuf);
                mat.SetInt("_GridW", _w);
                mat.SetInt("_GridH", _h);
                mat.SetFloat("_MinDepthMat", minDepth);
                mat.SetFloat("_LocalTideY", tideTargetLocalY);
            }

            UpdateWaterSandCoupling(Time.deltaTime);

            // 诊断回读
            _diagTimer -= Time.deltaTime;
            if (_diagTimer <= 0f)
            {
                ReadbackDiag();
                _diagTimer = 0.5f;
            }
        }

        void BindAll(int k)
        {
            compute.SetBuffer(k, "_Terrain", _terrainBuf);
            compute.SetBuffer(k, "_Depth", _depthBuf);
            compute.SetBuffer(k, "_Flux", _fluxBuf);
        }

        void UpdateWaterSandCoupling(float deltaTime)
        {
            if (_vol == null || _depthBuf == null || _fluxBuf == null) return;

            _erosionTimer += deltaTime;
            float interval = Mathf.Max(0.02f, erosionInterval);
            if (_erosionTimer < interval) return;

            float elapsed = Mathf.Min(_erosionTimer, interval * 4f);
            _erosionTimer = 0f;

            bool changed = false;
            if (wetnessDecay > 0f)
                changed |= _vol.DecayWetnessForDelta(wetnessDecay, elapsed);

            bool waterAffectsSand = erosionEnabled || waterWetnessPerSecond > 0f;
            if (waterAffectsSand)
            {
                PerfProbe.Begin("GPU.WaterReadback");
                if (_erosionDepth == null || _erosionDepth.Length != _cellCount)
                    _erosionDepth = new float[_cellCount];
                _depthBuf.GetData(_erosionDepth);

                float[] flux = null;
                if (flowErosionWeight > 0f && erosionEnabled)
                {
                    int fluxCount = _cellCount * 4;
                    if (_erosionFlux == null || _erosionFlux.Length != fluxCount)
                        _erosionFlux = new float[fluxCount];
                    _fluxBuf.GetData(_erosionFlux);
                    flux = _erosionFlux;
                }
                PerfProbe.End("GPU.WaterReadback");

                float erosionAmount = erosionEnabled ? erodePerSecond * elapsed : 0f;
                float wetnessAmount = waterWetnessPerSecond * elapsed;
                changed |= _vol.SurfaceErodeByWaterField(
                    _erosionDepth,
                    flux,
                    _w,
                    _h,
                    erosionAmount,
                    erodeBandAboveWater,
                    erodeBandBelowWater,
                    erosionMinDepth,
                    erosionDepthForFullStrength,
                    erosionFlowSpeedForFullStrength,
                    flowErosionWeight,
                    waterWetnessTarget,
                    wetnessAmount);
            }

            if (!changed) return;

            _vol.RebuildMesh();
            if (_vol.LastErodedPoints.Count > 0)
            {
                if (_particles == null) _particles = ErosionParticles.Create(transform);
                _particles.Emit(_vol.LastErodedPoints);
                _vol.CheckCollapse();
            }
        }

        // ===== 诊断: 供 F1 面板看水状态稳不稳 =====
        public struct WaterDiag
        {
            public float totalVolume;   // 总水量(m^3) —— 稳态下应趋于常数; 持续涨落=不守恒/炸
            public float maxDepth;      // 最大水深(m)
            public float avgDepth;      // 湿格平均水深
            public int wetCells;        // 湿格数
            public bool hasNaN;         // 是否出现 NaN/Inf(求解爆炸)
        }
        private WaterDiag _diag;
        private float[] _diagReadback;
        private float _diagTimer;
        public WaterDiag GetWaterDiag() => _diag;

        void ReadbackDiag()
        {
            if (_diagReadback == null || _diagReadback.Length != _cellCount)
                _diagReadback = new float[_cellCount];
            _depthBuf.GetData(_diagReadback);   // 同步回读(仅诊断, 0.5s一次)
            float total = 0, mx = 0, sum = 0; int wet = 0; bool nan = false;
            float cellArea = _cellSize * _cellSize;
            for (int i = 0; i < _cellCount; i++)
            {
                float d = _diagReadback[i];
                if (float.IsNaN(d) || float.IsInfinity(d)) { nan = true; continue; }
                if (d > minDepth) { wet++; sum += d; }
                if (d > mx) mx = d;
                total += d * cellArea;
            }
            _diag.totalVolume = total;
            _diag.maxDepth = mx;
            _diag.avgDepth = wet > 0 ? sum / wet : 0;
            _diag.wetCells = wet;
            _diag.hasNaN = nan;
        }

        void OnDestroy()
        {
            _terrainBuf?.Release();
            _depthBuf?.Release();
            _fluxBuf?.Release();
        }
    }
}
