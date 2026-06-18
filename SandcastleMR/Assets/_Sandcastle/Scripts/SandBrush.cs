using UnityEngine;

/// <summary>
/// 沙地笔刷：鼠标操控挖/堆/浇水。
/// - 左键：挖（降低高度）
/// - 右键：堆（增加高度）
/// - 中键：浇水（增加湿度）
/// - 滚轮 + Shift：调整笔刷大小
/// 
/// 单独挂在任意 GameObject 上（Bootstrap 会自动挂）。
/// 需要场景里有 SandTerrain 组件。
/// </summary>
public class SandBrush : MonoBehaviour
{
    [Header("笔刷参数")]
    public float brushRadius = 1f;
    public float minRadius = 0.2f;
    public float maxRadius = 5f;
    public float carveSpeed = 2f;
    public float pileSpeed = 2f;
    public float wetSpeed = 1.5f;

    [Header("视觉反馈")]
    public bool showBrushGizmo = true;

    private SandTerrain _terrain;
    private Camera _cam;
    private Vector3 _lastHitPos;
    private bool _hasHit;

    void Start()
    {
        _terrain = FindObjectOfType<SandTerrain>();
        _cam = Camera.main;
    }

    void Update()
    {
        if (_terrain == null || _cam == null) return;

        // Shift + 滚轮调整笔刷大小
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                brushRadius += scroll * 2f;
                brushRadius = Mathf.Clamp(brushRadius, minRadius, maxRadius);
            }
        }

        // 射线检测地形
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            _lastHitPos = hit.point;
            _hasHit = true;

            float dt = Time.deltaTime;

            bool qHeld = Input.GetKey(KeyCode.Q);
            bool eHeld = Input.GetKey(KeyCode.E);

            // 左键：
            //   默认 = 挖
            //   Q + 左键 = 堆
            //   E + 左键 = 浇水
            if (Input.GetMouseButton(0))
            {
                if (qHeld)
                {
                    _terrain.Pile(hit.point, brushRadius, pileSpeed * dt);
                }
                else if (eHeld)
                {
                    _terrain.Wet(hit.point, brushRadius, wetSpeed * dt);
                }
                else
                {
                    _terrain.Carve(hit.point, brushRadius, carveSpeed * dt);
                }
            }

            // 保留中键浇水（与 E 任选一）
            if (Input.GetMouseButton(2))
            {
                _terrain.Wet(hit.point, brushRadius, wetSpeed * dt);
            }
        }
        else
        {
            _hasHit = false;
        }
    }

    void OnDrawGizmos()
    {
        if (!showBrushGizmo || !_hasHit) return;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(_lastHitPos, brushRadius);
    }
}
