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
        [Tooltip("基础水位（世界 Y）")]
        public float baseWaterLevel = 0f;
        [Tooltip("涨潮峰值水位（高于基础水位多少米）")]
        public float waveAmplitude = 0.04f;

        [Header("周期")]
        [Tooltip("涨潮周期（秒）")]
        public float wavePeriod = 6f;
        [Tooltip("每个周期内浪头持续比例（0~1，0.3=30%时间在浪上）")]
        [Range(0.05f, 0.8f)]
        public float surgeRatio = 0.3f;

        [Header("侵蚀")]
        [Tooltip("浪头每秒侵蚀量（米）。SDF值往正方向加，表面后退")]
        public float erodePerSecond = 0.15f;
        [Tooltip("侵蚀作用范围: 离水位多少米内的体素受侵蚀")]
        public float erodeBandHeight = 1.0f;

        [Header("湿度")]
        [Tooltip("湿度蒸发速率（每秒）")]
        public float wetnessDecay = 0.1f;

        [Header("引用")]
        public SdfVolume sdfVolume;
        public SimpleWave simpleWave;
        private SandTerrain _terrain;

        private float _t;
        private float _currentLevel;
        private bool _eroding;

        public float CurrentWaterLevel => _currentLevel;

        void Start()
        {
            if (sdfVolume == null) sdfVolume = FindObjectOfType<SdfVolume>();
            if (simpleWave == null) simpleWave = FindObjectOfType<SimpleWave>();
            _terrain = FindObjectOfType<SandTerrain>();

            // 自动计算基础水位 = 沙地Y + initialHeight + 水面偏移
            if (_terrain != null && simpleWave != null)
            {
                baseWaterLevel = _terrain.transform.position.y + _terrain.initialHeight + simpleWave.heightAboveSand;
            }
            _currentLevel = baseWaterLevel;

            // 立即设置全局变量，避免第一帧闪烁
            Shader.SetGlobalFloat("_GlobalWaterY", baseWaterLevel + waveAmplitude);
            Shader.SetGlobalFloat("_GlobalWetTransition", 0.05f);
        }

        void Update()
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
            // 采用峰值水位，这样退潮后被打过的沙还是湿的，梦错位阐明
            float maxLevel = baseWaterLevel + waveAmplitude;
            Shader.SetGlobalFloat("_GlobalWaterY", maxLevel);
            Shader.SetGlobalFloat("_GlobalWetTransition", 0.05f);

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
                sdfVolume.ErodeBelowWater(_currentLevel, erodePerSecond * Time.deltaTime, erodeBandHeight);
                _eroding = true;

                // SandTerrain 也变湿（SDF 区域外的沙地）
                if (_terrain != null)
                {
                    _terrain.Wet(Vector3.zero, _terrain.size * 0.5f, 0.5f);
                }
            }
            else if (_eroding)
            {
                // 退潮时刷新一次 mesh，让积累的侵蚀显现
                sdfVolume.RebuildMesh();
                _eroding = false;
            }

            // 湿度蒸发
            if (sdfVolume != null)
                sdfVolume.DecayWetness(wetnessDecay);
        }
    }
}
