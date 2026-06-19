using UnityEngine;
using Sandcastle;

/// <summary>
/// 沙堡场景启动器：在场景中只挂一个空 GameObject 上挂这个脚本，
/// 运行时会自动创建：沙滩平面、太阳光、轨道相机。
/// 这样不依赖手动配置 Hierarchy，便于通过 Git 同步。
/// </summary>
[DefaultExecutionOrder(-100)]
public class SandcastleBootstrap : MonoBehaviour
{
    [Header("沙箱")]
    [Tooltip("沙地桌面尺寸（米）。还原原始 20m。后续 MR 再缩到 40cm")]
    public Vector2 beachSize = new Vector2(20f, 20f);
    public Color sandColor = new Color(0.92f, 0.82f, 0.62f);

    [Header("光照")]
    public Color sunColor = new Color(1f, 0.95f, 0.85f);
    public float sunIntensity = 1.2f;
    public Vector3 sunEuler = new Vector3(50f, -30f, 0f);

    [Header("环境")]
    public Color skyColor = new Color(0.55f, 0.78f, 0.92f);
    public Color equatorColor = new Color(0.85f, 0.92f, 0.95f);
    public Color groundColor = new Color(0.75f, 0.65f, 0.5f);

    void Awake()
    {
        // 归零根 transform：保证所有程序生成的子物体（SDF沙箱/水面/相机）
        // 落在预期世界坐标。否则根物体被拖动过会让沙面偏移、与水面/相机错位。
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        BuildLighting();
        BuildBeach();
        BuildWater();
        BuildCamera();
    }

    void BuildLighting()
    {
        // 太阳
        var sunGo = new GameObject("Sun");
        sunGo.transform.SetParent(transform, false);
        sunGo.transform.eulerAngles = sunEuler;
        var light = sunGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = sunColor;
        light.intensity = sunIntensity;
        light.shadows = LightShadows.Soft;

        // 环境光（简单天/地渐变）
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = skyColor;
        RenderSettings.ambientEquatorColor = equatorColor;
        RenderSettings.ambientGroundColor = groundColor;

        // 雾效（柔化远处边缘）
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = equatorColor;
        RenderSettings.fogDensity = 0.015f;
    }

    void BuildBeach()
    {
        // 不再创建高度场 SandTerrain。沙地全部交给全局 SDF（有厚度的沙层）。

        // 构件放置器（旧高度场模式）已退场，全局 SDF 为唯一系统

        // Debug UI
        var dbgGo = new GameObject("DebugUI");
        dbgGo.transform.SetParent(transform, false);
        dbgGo.AddComponent<SandcastleDebugUI>();

        // 全局 SDF 沙箱：还原换方案前尺度——中心 Y=0.5，体积 5×1.5×5，范围 -0.25~+1.25
        var sdfGo = new GameObject("SdfVolume");
        sdfGo.transform.SetParent(transform, false);
        sdfGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        sdfGo.AddComponent<MeshFilter>();
        var sdfMr = sdfGo.AddComponent<MeshRenderer>();
        Shader sdfSandShader = Shader.Find("Sandcastle/Sand");
        if (sdfSandShader == null) sdfSandShader = Shader.Find("Universal Render Pipeline/Lit");
        var sdfMat = new Material(sdfSandShader);
        sdfMat.SetColor("_BaseColor", sandColor);
        sdfMr.sharedMaterial = sdfMat;
        var volume = sdfGo.AddComponent<SdfVolume>();
        // size 用 SdfVolume 默认 (5,1.5,5)，不覆写
        sdfGo.AddComponent<SdfVolumeBoundsVisualizer>();

        // 沙面 collider（供放置射线命中），位于沙面 世界 Y=-0.10
        // 相对体积中心 0.5，沙面 -0.10 → 局部 -0.60
        var sdfFloor = new GameObject("SdfFloor");
        sdfFloor.transform.SetParent(sdfGo.transform, false);
        sdfFloor.transform.localPosition = new Vector3(0f, -0.60f, 0f);
        var box = sdfFloor.AddComponent<BoxCollider>();
        box.size = new Vector3(beachSize.x, 0.02f, beachSize.y);

        // SDF 放置器（唯一放置器，默认启用）
        var sdfPlacerGo = new GameObject("SdfPiecePlacer");
        sdfPlacerGo.transform.SetParent(transform, false);
        sdfPlacerGo.AddComponent<SdfPiecePlacer>();

        // 样条沙堤放置器（按 G 绘制）
        var splineGo = new GameObject("SplineWallPlacer");
        splineGo.transform.SetParent(transform, false);
        splineGo.AddComponent<SplineWallPlacer>();

        // 脚印气氛效果（按住 F 沿鼠标移动留脚印）
        // 先挂通用法线贴花系统（FootprintManager 依赖它）
        var decalGo = new GameObject("SandDecalSystem");
        decalGo.transform.SetParent(transform, false);
        decalGo.AddComponent<SandDecalSystem>();

        var footGo = new GameObject("FootprintManager");
        footGo.transform.SetParent(transform, false);
        footGo.AddComponent<FootprintManager>();
    }

    void BuildWater()
    {
        var waterGo = new GameObject("Water");
        waterGo.transform.SetParent(transform, false);
        waterGo.AddComponent<SimpleWave>();

        // 海浪侵蚀模拟器
        var waveGo = new GameObject("WaveSimulator");
        waveGo.transform.SetParent(transform, false);
        waveGo.AddComponent<WaveSimulator>();
    }

    void BuildCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = skyColor;
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;

        var orbit = cam.GetComponent<OrbitCamera>();
        if (orbit == null) orbit = cam.gameObject.AddComponent<OrbitCamera>();
        orbit.targetPoint = Vector3.zero;
        orbit.distance = 12f;
        orbit.minDistance = 2f;
        orbit.maxDistance = 30f;
    }
}
