using UnityEngine;

/// <summary>
/// 运行时 Debug 面板：左上角调试拉杆。
/// - 融合带高度 / 偏移 / 噪声 / 颜色
/// - 一键重新应用到所有已放置的构件
/// 
/// 按 F1 切换显示/隐藏。
/// </summary>
public class SandcastleDebugUI : MonoBehaviour
{
    [Header("默认融合参数")]
    public float blendHeight = 0.4f;
    public float blendOffset = 0.02f;
    public float noiseScale = 12f;
    public float noiseStrength = 0.1f;
    public Color baseColor = new Color(0.88f, 0.78f, 0.58f);
    public Color sandColor = new Color(0.45f, 0.32f, 0.20f);

    private bool _show = true;
    private Vector2 _scroll;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) _show = !_show;
    }

    void OnGUI()
    {
        if (!_show) return;

        GUI.skin.label.fontSize = 14;
        GUI.skin.button.fontSize = 14;
        GUI.skin.box.fontSize = 14;

        GUILayout.BeginArea(new Rect(10, 10, 340, Screen.height - 20), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);

        GUILayout.Label("=== Castle Piece 融合调试 ===");
        GUILayout.Label("F1 = 显示/隐藏");
        GUILayout.Space(8);

        blendHeight = LabeledSlider("Blend Height (融合带)", blendHeight, 0.05f, 3f);
        blendOffset = LabeledSlider("Blend Offset (基准偏移)", blendOffset, -0.5f, 0.5f);
        noiseScale = LabeledSlider("Noise Scale (噪声密度)", noiseScale, 1f, 50f);
        noiseStrength = LabeledSlider("Noise Strength (噪声强度)", noiseStrength, 0f, 0.3f);

        GUILayout.Space(8);
        GUILayout.Label("Piece Color (构件本体色)");
        baseColor = ColorSlider(baseColor);

        GUILayout.Space(4);
        GUILayout.Label("Sand Color (融合目标色)");
        sandColor = ColorSlider(sandColor);

        GUILayout.Space(8);
        if (GUILayout.Button("应用到所有已放置构件"))
            ApplyToAllPieces();

        GUILayout.Space(8);
        GUILayout.Label("操作提示：");
        GUILayout.Label("左键 = 放塔");
        GUILayout.Label("X+左键 = 删塔");
        GUILayout.Label("R = 旋转预览");
        GUILayout.Label("+/- = 缩放预览");
        GUILayout.Label("右键拖 = 视角，滚轮 = 缩放");

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    float LabeledSlider(string label, float value, float min, float max)
    {
        GUILayout.Label($"{label}: {value:F3}");
        float v = GUILayout.HorizontalSlider(value, min, max);
        if (!Mathf.Approximately(v, value))
        {
            ApplyFloat(MapLabelToProperty(label), v);
        }
        return v;
    }

    string MapLabelToProperty(string label)
    {
        if (label.StartsWith("Blend Height")) return "_BlendHeight";
        if (label.StartsWith("Blend Offset")) return "_BlendOffset";
        if (label.StartsWith("Noise Scale")) return "_NoiseScale";
        if (label.StartsWith("Noise Strength")) return "_NoiseStrength";
        return null;
    }

    Color ColorSlider(Color c)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("R", GUILayout.Width(15));
        c.r = GUILayout.HorizontalSlider(c.r, 0f, 1f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("G", GUILayout.Width(15));
        c.g = GUILayout.HorizontalSlider(c.g, 0f, 1f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("B", GUILayout.Width(15));
        c.b = GUILayout.HorizontalSlider(c.b, 0f, 1f);
        GUILayout.EndHorizontal();
        return c;
    }

    void ApplyFloat(string prop, float value)
    {
        if (string.IsNullOrEmpty(prop)) return;
        foreach (var piece in FindObjectsOfType<CastlePiece>())
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
                }
            }
        }
    }

    void ApplyToAllPieces()
    {
        foreach (var piece in FindObjectsOfType<CastlePiece>())
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_BlendHeight")) mat.SetFloat("_BlendHeight", blendHeight);
                    if (mat.HasProperty("_BlendOffset")) mat.SetFloat("_BlendOffset", blendOffset);
                    if (mat.HasProperty("_NoiseScale")) mat.SetFloat("_NoiseScale", noiseScale);
                    if (mat.HasProperty("_NoiseStrength")) mat.SetFloat("_NoiseStrength", noiseStrength);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
                    if (mat.HasProperty("_SandColor")) mat.SetColor("_SandColor", sandColor);
                }
            }
        }
    }
}
