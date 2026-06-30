using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 海浪模拟器：
    /// - 周期性水位涨落（脉冲式涨潮）
    /// - 涨潮时调用 SdfVolume.ErodeBelowWater 侵蚀水下 SDF 体素
    /// - SimpleWave 视觉跟随当前水位
    /// 
    /// 玩法：玩家堆的沙堡，每次涨潮被冲掉一层，越来越矮直至崩塌。
    /// </summary>
    public class WaveSimulator : MonoBehaviour
    {
        [Header("水位")]
        [Tooltip("静止水面相对沙面的高度（米）。运行时从 SDF 沙面世界 Y 推算出 baseWaterLevel")]
        public float waterAboveSand = 0.02f;
        [Tooltip("基础水位（世界 Y）。运行时由 SDF 沙面 + waterAboveSand 自动计算覆写")]
        public float baseWaterLevel = -0.08f;
        [Tooltip("涨潮峰值水位（高于基础水位多少米）")]
        public float waveAmplitude = 0.06f;

        [Header("周期")]
        [Tooltip("涨潮周期（秒）")]
        public float wavePeriod = 5f;
        [Tooltip("每个周期内浪头持续比例（0~1，0.3=30%时间在浪上）")]
        [Range(0.05f, 0.8f)]
        public float surgeRatio = 0.3f;

        [Header("侵蚀")]
        [Tooltip("浪头每秒侵蚀量（米）。SDF值往正方向加，表面后退")]
        public float erodePerSecond = 0.02f;
        [Tooltip("侵蚀作用范围: 离水位多少米内的体素受侵蚀")]
        public float erodeBandHeight = 1.0f;
        [Tooltip("侵蚀过程中多久重建一次 mesh（秒）。越小衰减越连贯但越卡")]
        public float rebuildInterval = 0.25f;

        [Header("湿度")]
        [Tooltip("湿度蒸发速率（每秒）")]
        public float wetnessDecay = 0.1f;

        [Header("引用")]
        public SdfVolume sdfVolume;
        public SimpleWave simpleWave;
        public TideController tideController;

        private float _t;
        private float _currentLevel;
        private bool _eroding;
        private float _rebuildTimer;
        private ErosionParticles _particles;
        private readonly System.Collections.Generic.List<Vector3> _collapsePoints = new System.Collections.Generic.List<Vector3>(48);

        public float CurrentWaterLevel => _currentLevel;

        void Start()
        {
            if (sdfVolume == null) sdfVolume = FindObjectOfType<SdfVolume>();
            if (simpleWave == null) simpleWave = FindObjectOfType<SimpleWave>();
            if (tideController == null) tideController = FindObjectOfType<TideController>();
            _particles = ErosionParticles.Create(transform);
            // 延迟一帧初始化，让 SimpleWave.Start 先完成定位
            StartCoroutine(InitWaterLevel());
        }

        System.Collections.IEnumerator InitWaterLevel()
        {
            yield return null;
            // 从 SDF 沙面的实际世界高度推算水位，这样不管根物体偏移到哪，水面永远贴着沙面
            if (sdfVolume != null)
                baseWaterLevel = sdfVolume.SandSurfaceWorldY + waterAboveSand;
            _currentLevel = baseWaterLevel;
            if (simpleWave != null) simpleWave.restWorldY = baseWaterLevel;
            Shader.SetGlobalFloat("_GlobalWaterY", baseWaterLevel);
            Shader.SetGlobalFloat("_GlobalWetTransition", 0.05f);
            float sandY = sdfVolume != null ? sdfVolume.SandSurfaceWorldY : 0f;
            Debug.Log($"[Wave] 初始化: SDF沙面Y={sandY:F3}, baseWaterLevel={baseWaterLevel:F3}");
        }

        void Update()
        {
            if (tideController != null)
            {
                _currentLevel = tideController.CurrentWaterLevel;
            }
            else
            {
                _t += Time.deltaTime;
                float phase = (_t % wavePeriod) / wavePeriod; // 0~1

                // 一个周期内：前 surgeRatio 时间是浪起+浪退，后面平静
                float surge = 0f;
                if (phase < surgeRatio)
                {
                    float p = phase / surgeRatio;
                    surge = Mathf.Sin(p * Mathf.PI); // 0→1→0
                }

                _currentLevel = baseWaterLevel + surge * waveAmplitude;

                // 全局水位传给所有 Sand shader（顶点低Y会自动显示为湿沙）
                // 用当前真实水位，这样静止时湿沙线锁在静止水面，不会被峰值抬高
                Shader.SetGlobalFloat("_GlobalWaterY", _currentLevel);
                Shader.SetGlobalFloat("_GlobalWetTransition", 0.05f);
            }
            // 同步水面视觉
            if (simpleWave != null)
            {
                Vector3 pos = simpleWave.transform.position;
                pos.y = _currentLevel;
                simpleWave.transform.position = pos;
            }

            // 浪头时段触发侵蚀 + 湿润
            if (surge > 0.3f && sdfVolume != null)
            {
                sdfVolume.SurfaceErode(_currentLevel, erodePerSecond * Time.deltaTime, erodeBandHeight);
                _eroding = true;

                // 碎屑粒子特效：在被冲掉的表面体素位置喷沙
                if (_particles != null)
                    _particles.Emit(sdfVolume.LastErodedPoints);

                // 侵蚀过程中阶段性重建 mesh，让“化开”过程连贯可见，而不是只在退潮才跳变
                _rebuildTimer += Time.deltaTime;
                if (_rebuildTimer >= rebuildInterval)
                {
                    sdfVolume.RebuildMesh();
                    // 每次重建后立即检测无支撑残块并移除（无需等退潮）
                    int removed = sdfVolume.RemoveUnsupported(_collapsePoints);
                    if (removed > 0)
                    {
                        sdfVolume.RebuildMesh();
                        if (_particles != null) _particles.Emit(_collapsePoints);
                    }
                    _rebuildTimer = 0f;
                }
            }
            else if (_eroding)
            {
                // 退潮时刷新一次 mesh，让积累的侵蚀显现
                sdfVolume.RebuildMesh();
                // 连通域检测：没支撑的残块立即移除 + 掉渣粒子
                int removed = sdfVolume.RemoveUnsupported(_collapsePoints);
                if (removed > 0)
                {
                    sdfVolume.RebuildMesh();
                    if (_particles != null) _particles.Emit(_collapsePoints);
                    Debug.Log($"[Wave] 无支撑塔塌: 移除 {removed} 个体素");
                }
                _eroding = false;
                _rebuildTimer = 0f;
            }

            // 湿度蒸发
            if (sdfVolume != null)
                sdfVolume.DecayWetness(wetnessDecay);
        }
    }
}
