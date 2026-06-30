using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 统一的潮汐与海浪控制器。
    /// 计算周期性水位涨落，更新全局 Shader 水位，并向流体和传统波浪系统提供统一数据。
    /// </summary>
    public class TideController : MonoBehaviour, ITideProvider
    {
        [Header("水位")]
        [Tooltip("静止水面相对沙面的高度（米）。运行时由 SDF 沙面 + waterAboveSand 自动计算 baseWaterLevel")]
        public float waterAboveSand = 0.02f;
        [Tooltip("基础水位（世界 Y）。由 SdfVolume 自动计算或在此覆写")]
        public float baseWaterLevel = -0.08f;
        [Tooltip("涨潮峰值水位（高于基础水位多少米）")]
        public float waveAmplitude = 0.06f;

        [Header("周期")]
        [Tooltip("涨潮周期（秒）")]
        public float wavePeriod = 5f;
        [Tooltip("每个周期内浪头持续比例（0~1，0.3=30%时间在浪上）")]
        [Range(0.05f, 0.8f)]
        public float surgeRatio = 0.3f;

        [Header("引用")]
        public SdfVolume sdfVolume;

        private float _t;
        private float _currentLevel;

        public float CurrentWaterLevel => _currentLevel;

        public float CurrentTideLocalY
        {
            get
            {
                if (sdfVolume == null) return 0f;
                // 将世界坐标下的水位 Y 转换成 SdfVolume 局部 Y 坐标
                Vector3 localPos = sdfVolume.WorldToLocal(new Vector3(0f, _currentLevel, 0f));
                return localPos.y;
            }
        }

        void Start()
        {
            if (sdfVolume == null) sdfVolume = GetComponent<SdfVolume>();
            if (sdfVolume == null) sdfVolume = FindObjectOfType<SdfVolume>();

            // 延迟一帧初始化，确保 SdfVolume 的 SandSurfaceWorldY 已就绪
            StartCoroutine(InitTide());
        }

        System.Collections.IEnumerator InitTide()
        {
            yield return null;
            if (sdfVolume != null)
            {
                baseWaterLevel = sdfVolume.SandSurfaceWorldY + waterAboveSand;
            }
            _currentLevel = baseWaterLevel;
            Shader.SetGlobalFloat("_GlobalWaterY", _currentLevel);
            Shader.SetGlobalFloat("_GlobalWetTransition", 0.05f);
        }

        void Update()
        {
            _t += Time.deltaTime;
            float phase = (_t % wavePeriod) / wavePeriod; // 0~1

            float surge = 0f;
            if (phase < surgeRatio)
            {
                float p = phase / surgeRatio;
                surge = Mathf.Sin(p * Mathf.PI); // 0→1→0
            }

            _currentLevel = baseWaterLevel + surge * waveAmplitude;

            // 全局水位传给所有 Sand shader（顶点低Y会自动显示为湿沙）
            Shader.SetGlobalFloat("_GlobalWaterY", _currentLevel);
            Shader.SetGlobalFloat("_GlobalWetTransition", 0.05f);
        }
    }
}
