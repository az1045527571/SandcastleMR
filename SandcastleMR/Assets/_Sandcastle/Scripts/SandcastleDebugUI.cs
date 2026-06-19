using UnityEngine;
using Sandcastle;

/// <summary>
/// 运行时 Debug：左上角拉杆面板。
/// F1 切换显示/隐藏。全局 SDF 沙箱版。
/// </summary>
public class SandcastleDebugUI : MonoBehaviour
{
    public float waterLevel = 0.04f;  // 静止水面世界 Y

    private bool _show = true;
    private SimpleWave _wave;
    private WaveSimulator _waveSim;

    void Start()
    {
        _wave = FindObjectOfType<SimpleWave>();
        _waveSim = FindObjectOfType<WaveSimulator>();
        if (_waveSim != null) waterLevel = _waveSim.baseWaterLevel;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) _show = !_show;
    }

    void OnGUI()
    {
        if (!_show) return;

        GUILayout.BeginArea(new Rect(10, 10, 340, 220), GUI.skin.box);
        GUILayout.Label("F1=隐藏   2=放置 B=球 V=浇水 X+左键=删");

        GUILayout.Label($"水位 Water Level: {waterLevel:F3} m");
        float wh = GUILayout.HorizontalSlider(waterLevel, 0f, 0.16f);
        if (!Mathf.Approximately(wh, waterLevel))
        {
            waterLevel = wh;
            ApplyWaterLevel();
        }

        if (_waveSim != null)
        {
            GUILayout.Label($"涨潮幅度 Amplitude: {_waveSim.waveAmplitude:F3} m");
            _waveSim.waveAmplitude = GUILayout.HorizontalSlider(_waveSim.waveAmplitude, 0f, 0.12f);

            GUILayout.Label($"侵蚀速度 Erode/s: {_waveSim.erodePerSecond:F4} m");
            _waveSim.erodePerSecond = GUILayout.HorizontalSlider(_waveSim.erodePerSecond, 0f, 0.02f);

            GUILayout.Label($"潮汐周期 Period: {_waveSim.wavePeriod:F1} s");
            _waveSim.wavePeriod = GUILayout.HorizontalSlider(_waveSim.wavePeriod, 1f, 12f);
        }

        GUILayout.EndArea();
    }

    void ApplyWaterLevel()
    {
        if (_wave != null)
        {
            _wave.restWorldY = waterLevel;
            Vector3 pos = _wave.transform.position;
            pos.y = waterLevel;
            _wave.transform.position = pos;
        }
        if (_waveSim != null)
            _waveSim.baseWaterLevel = waterLevel;
    }
}
