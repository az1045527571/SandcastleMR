using UnityEngine;
using Sandcastle;

/// <summary>
/// 运行时 Debug：左上角拉杆面板。
/// F1 切换显示/隐藏。全局 SDF 沙箱版。
/// </summary>
public class SandcastleDebugUI : MonoBehaviour
{
    public float waterLevel = -0.08f;  // 静止水面世界 Y

    private bool _show = true;
    private SimpleWave _wave;
    private WaveSimulator _waveSim;

    // 实时帧率（指数平滑）
    private float _smoothDt;
    private GpuSandRenderer _gpuSand;

    void Start()
    {
        _wave = FindObjectOfType<SimpleWave>();
        _waveSim = FindObjectOfType<WaveSimulator>();
        _gpuSand = FindObjectOfType<GpuSandRenderer>();
        if (_waveSim != null) waterLevel = _waveSim.baseWaterLevel;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) _show = !_show;
        // 指数平滑帧时间
        _smoothDt = Mathf.Lerp(_smoothDt, Time.unscaledDeltaTime, 0.1f);
    }

    void OnGUI()
    {
        if (!_show) return;

        GUILayout.BeginArea(new Rect(10, 10, 420, 620), GUI.skin.box);
        // 实时帧率
        float fps = _smoothDt > 1e-5f ? 1f / _smoothDt : 0f;
        string path = _gpuSand != null && _gpuSand.useGpu ? "GPU" : "CPU";
        GUILayout.Label($"FPS: {fps:F1}  ({_smoothDt * 1000f:F1} ms)  沙子路径: {path}  [F2切换]");
        GUILayout.Label("F1=隐藏   2=放置 B=球 V=浇水 X+左键=删");

        // ===== 性能分段计时(定位侵蚀卡在哪一步) =====
        GUILayout.Label("─── 性能分段 (ms, 平滑) ───");
        GUILayout.Label(Sandcastle.PerfProbe.Report());
        if (GUILayout.Button("重置计时计数"))
            Sandcastle.PerfProbe.Reset();

        // ===== 坐标诊断 =====
        GUILayout.Label("─── 坐标诊断 (世界 Y) ───");

        var volume = FindObjectOfType<SdfVolume>();
        if (volume != null)
        {
            Vector3 vc = volume.transform.position;
            Vector3 sz = volume.size;
            float volBottom = vc.y - sz.y * 0.5f;
            float volTop = vc.y + sz.y * 0.5f;
            float sandTop = volBottom + volume.sandLayerThickness;
            GUILayout.Label($"SDF 体积中心: {vc.x:F3}, {vc.y:F3}, {vc.z:F3}");
            GUILayout.Label($"SDF 体积范围 Y: {volBottom:F3} ~ {volTop:F3}");
            GUILayout.Label($"SDF 体积尺寸: {sz.x:F2} × {sz.y:F2} × {sz.z:F2}");
            GUILayout.Label($"沙层厚度: {volume.sandLayerThickness:F3}  → 沙面 Y={sandTop:F3}");

            var mf = volume.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds b = mf.sharedMesh.bounds; // 本地
                Vector3 wMin = volume.transform.TransformPoint(b.min);
                Vector3 wMax = volume.transform.TransformPoint(b.max);
                GUILayout.Label($"SDF mesh 顶数: {mf.sharedMesh.vertexCount}");
                GUILayout.Label($"SDF mesh 世界 Y: {wMin.y:F3} ~ {wMax.y:F3}");
            }
            else
            {
                GUILayout.Label("SDF mesh: 空 (未生成!)");
            }
        }
        else
        {
            GUILayout.Label("SdfVolume: 未找到!");
        }

        if (_wave != null)
        {
            Vector3 wp = _wave.transform.position;
            GUILayout.Label($"水面实际位置: {wp.x:F3}, {wp.y:F3}, {wp.z:F3}");
            GUILayout.Label($"水面平面边长: {_wave.size:F2}");
        }
        if (_waveSim != null)
        {
            GUILayout.Label($"当前水位 CurrentLevel: {_waveSim.CurrentWaterLevel:F3}");
        }
        float globalWaterY = Shader.GetGlobalFloat("_GlobalWaterY");
        GUILayout.Label($"Shader _GlobalWaterY: {globalWaterY:F3}");

        var cam = Camera.main;
        if (cam != null)
            GUILayout.Label($"相机位置: {cam.transform.position.x:F2}, {cam.transform.position.y:F2}, {cam.transform.position.z:F2}");

        GUILayout.Space(6);
        GUILayout.Label("─── 调节 ───");

        GUILayout.Label($"水位 Water Level: {waterLevel:F3} m");
        float wh = GUILayout.HorizontalSlider(waterLevel, -0.20f, 0.30f);
        if (!Mathf.Approximately(wh, waterLevel))
        {
            waterLevel = wh;
            ApplyWaterLevel();
        }

        if (_waveSim != null)
        {
            GUILayout.Label($"涨潮幅度 Amplitude: {_waveSim.waveAmplitude:F3} m");
            _waveSim.waveAmplitude = GUILayout.HorizontalSlider(_waveSim.waveAmplitude, 0f, 0.20f);

            GUILayout.Label($"侵蚀速度 Erode/s: {_waveSim.erodePerSecond:F4} m");
            _waveSim.erodePerSecond = GUILayout.HorizontalSlider(_waveSim.erodePerSecond, 0f, 0.08f);

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
