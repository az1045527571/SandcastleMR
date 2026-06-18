using UnityEngine;
using Sandcastle;

/// <summary>
/// 运行时 Debug：左上角拉杆面板。
/// F1 切换显示/隐藏。
/// </summary>
public class SandcastleDebugUI : MonoBehaviour
{
    public float blendHeight = 0.4f;
    public float sinkDepth = 0.18f;
    public float waterHeight = 0.02f;  // 水面高于沙地多少米

    private bool _show = true;
    private SimpleWave _wave;
    private SandTerrain _terrain;
    private WaveSimulator _waveSim;

    void Start()
    {
        _wave = FindObjectOfType<SimpleWave>();
        _terrain = FindObjectOfType<SandTerrain>();
        _waveSim = FindObjectOfType<WaveSimulator>();
        if (_wave != null) waterHeight = _wave.heightAboveSand;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) _show = !_show;
    }

    void OnGUI()
    {
        if (!_show) return;

        GUILayout.BeginArea(new Rect(10, 10, 320, 180), GUI.skin.box);
        GUILayout.Label("F1=隐藏");

        GUILayout.Label($"Blend Height: {blendHeight:F2}");
        float bh = GUILayout.HorizontalSlider(blendHeight, 0.05f, 3f);
        if (!Mathf.Approximately(bh, blendHeight))
        {
            blendHeight = bh;
            ApplyBlendHeight();
        }

        GUILayout.Label($"Sink Depth: {sinkDepth:F2}");
        sinkDepth = GUILayout.HorizontalSlider(sinkDepth, 0f, 0.5f);

        GUILayout.Label($"Water Height: {waterHeight:F3} m (相对沙地)");
        float wh = GUILayout.HorizontalSlider(waterHeight, -0.05f, 0.30f);
        if (!Mathf.Approximately(wh, waterHeight))
        {
            waterHeight = wh;
            ApplyWaterHeight();
        }

        GUILayout.EndArea();
    }

    public float GetSinkDepth() => sinkDepth;

    void ApplyBlendHeight()
    {
        foreach (var piece in FindObjectsOfType<CastlePiece>())
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_BlendHeight"))
                        mat.SetFloat("_BlendHeight", blendHeight);
                }
            }
        }
    }

    void ApplyWaterHeight()
    {
        if (_wave != null)
        {
            _wave.heightAboveSand = waterHeight;
            // 立即更新位置
            if (_terrain != null)
            {
                Vector3 pos = _wave.transform.position;
                pos.y = _terrain.transform.position.y + _terrain.initialHeight + waterHeight;
                _wave.transform.position = pos;
            }
        }
        // 同步 WaveSimulator 的基础水位
        if (_waveSim != null && _terrain != null)
        {
            _waveSim.baseWaterLevel = _terrain.transform.position.y + _terrain.initialHeight + waterHeight;
        }
    }
}
