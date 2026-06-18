using UnityEngine;

/// <summary>
/// 按 1 = 旧模式（PiecePlacer + 法线融合）
/// 按 2 = SDF 模式（SdfPiecePlacer + 体积融合）
/// </summary>
public class PlacerModeSwitcher : MonoBehaviour
{
    private PiecePlacer _oldPlacer;
    private SdfPiecePlacer _sdfPlacer;

    void Start()
    {
        _oldPlacer = FindObjectOfType<PiecePlacer>();
        _sdfPlacer = FindObjectOfType<SdfPiecePlacer>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (_oldPlacer) _oldPlacer.enabled = true;
            if (_sdfPlacer) _sdfPlacer.enabled = false;
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            if (_oldPlacer) _oldPlacer.enabled = false;
            if (_sdfPlacer) _sdfPlacer.enabled = true;
        }
    }
}
