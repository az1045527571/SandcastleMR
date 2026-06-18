using UnityEngine;

/// <summary>
/// 运行时 Debug：左上角只显示 Blend Height 拉杆。
/// F1 切换显示/隐藏。
/// </summary>
public class SandcastleDebugUI : MonoBehaviour
{
    public float blendHeight = 0.4f;

    private bool _show = true;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) _show = !_show;
    }

    void OnGUI()
    {
        if (!_show) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 60), GUI.skin.box);
        GUILayout.Label($"Blend Height: {blendHeight:F2}  (F1=hide)");
        float v = GUILayout.HorizontalSlider(blendHeight, 0.05f, 3f);
        if (!Mathf.Approximately(v, blendHeight))
        {
            blendHeight = v;
            ApplyToAll();
        }
        GUILayout.EndArea();
    }

    void ApplyToAll()
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
}
