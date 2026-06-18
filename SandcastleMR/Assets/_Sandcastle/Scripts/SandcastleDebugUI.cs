using UnityEngine;

/// <summary>
/// 运行时 Debug：左上角只显示 Blend Height 拉杆。
/// F1 切换显示/隐藏。
/// </summary>
public class SandcastleDebugUI : MonoBehaviour
{
    public float blendHeight = 0.4f;
    public float sinkDepth = 0.18f;

    private bool _show = true;
    private PiecePlacer _placer;

    void Start()
    {
        _placer = FindObjectOfType<PiecePlacer>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) _show = !_show;
    }

    void OnGUI()
    {
        if (!_show) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 100), GUI.skin.box);

        GUILayout.Label($"Blend Height: {blendHeight:F2}");
        float bh = GUILayout.HorizontalSlider(blendHeight, 0.05f, 3f);
        if (!Mathf.Approximately(bh, blendHeight))
        {
            blendHeight = bh;
            ApplyBlendHeight();
        }

        GUILayout.Label($"Sink Depth: {sinkDepth:F2}");
        sinkDepth = GUILayout.HorizontalSlider(sinkDepth, 0f, 0.5f);

        GUILayout.Label("F1=隐藏");
        GUILayout.EndArea();
    }

    public float GetSinkDepth()
    {
        return sinkDepth;
    }

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
}
