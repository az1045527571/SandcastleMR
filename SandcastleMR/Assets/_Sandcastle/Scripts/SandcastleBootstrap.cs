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
    [Header("沙滩")]
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
        var beach = new GameObject("SandTerrain");
        beach.transform.SetParent(transform, false);
        var mf = beach.AddComponent<MeshFilter>();
        var mr = beach.AddComponent<MeshRenderer>();
        var mc = beach.AddComponent<MeshCollider>();

        // 提前赋材质，避免 SandTerrain.Awake 创建重复材质
        Shader sandShader = Shader.Find("Sandcastle/Sand");
        Material mat;
        if (sandShader != null)
        {
            mat = new Material(sandShader);
            mat.SetColor("_BaseColor", sandColor);
        }
        else
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", sandColor);
            mat.SetFloat("_Smoothness", 0.1f);
        }
        mr.sharedMaterial = mat;

        var terrain = beach.AddComponent<SandTerrain>();
        terrain.size = Mathf.Max(beachSize.x, beachSize.y);

        // 挂上构件放置器
        var placerGo = new GameObject("PiecePlacer");
        placerGo.transform.SetParent(transform, false);
        placerGo.AddComponent<PiecePlacer>();

        // Debug UI
        var dbgGo = new GameObject("DebugUI");
        dbgGo.transform.SetParent(transform, false);
        dbgGo.AddComponent<SandcastleDebugUI>();

        // SDF 体积系统
        var sdfGo = new GameObject("SdfVolume");
        sdfGo.transform.SetParent(transform, false);
        sdfGo.transform.localPosition = new Vector3(0f, 0.5f, 0f); // 体积中心在沙面上方0.5m，包含沙面及上方1m
        sdfGo.AddComponent<MeshFilter>();
        var sdfMr = sdfGo.AddComponent<MeshRenderer>();
        Shader sdfSandShader = Shader.Find("Sandcastle/Sand");
        if (sdfSandShader == null) sdfSandShader = Shader.Find("Universal Render Pipeline/Lit");
        sdfMr.sharedMaterial = new Material(sdfSandShader);
        sdfGo.AddComponent<SdfVolume>();

        // SDF 放置器（按 2 切换到 SDF 模式）
        var sdfPlacerGo = new GameObject("SdfPiecePlacer");
        sdfPlacerGo.transform.SetParent(transform, false);
        var sdfPlacer = sdfPlacerGo.AddComponent<SdfPiecePlacer>();
        sdfPlacer.enabled = false; // 默认关闭，按 2 开启

        // 模式切换器
        var switchGo = new GameObject("ModeSwitcher");
        switchGo.transform.SetParent(transform, false);
        switchGo.AddComponent<PlacerModeSwitcher>();
    }

    void BuildWater()
    {
        var waterGo = new GameObject("Water");
        waterGo.transform.SetParent(transform, false);
        waterGo.AddComponent<SimpleWave>();
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
        // 轨道相机不跟 Shift+滚轮冲突了，直接简化
        orbit.targetPoint = Vector3.zero;
        orbit.distance = 12f;
    }
}
