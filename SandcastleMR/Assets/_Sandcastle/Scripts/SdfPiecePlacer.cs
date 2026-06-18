using UnityEngine;
using Sandcastle;

/// <summary>
/// SDF 模式的构件放置器。
/// 按 2 切换到此模式（原来的按 1 保留旧系统）。
/// 左键在 SDF 体积内放置球形 SdfPiece。
/// </summary>
/// 按 2 切换到此模式（原来的按 1 保留旧系统）。
/// 左键在 SDF 体积内放置球形 SdfPiece。
/// </summary>
public class SdfPiecePlacer : MonoBehaviour
{
    [Header("SDF 球参数")]
    public float defaultRadius = 0.5f;
    public float minRadius = 0.05f;
    public float maxRadius = 2f;
    public float radiusStep = 0.05f;

    private Camera _cam;
    private SdfVolume _volume;
    private SandTerrain _terrain;
    private float _currentRadius;
    private GameObject _preview;

    void Start()
    {
        _cam = Camera.main;
        _volume = FindObjectOfType<SdfVolume>();
        _terrain = FindObjectOfType<SandTerrain>();
        _currentRadius = defaultRadius;
        CreatePreview();
    }

    void Update()
    {
        if (_cam == null || _volume == null) return;
        if (!enabled) return;

        // 滚轮调球半径（在 SDF 模式下会抢占相机缩放，这里只在有命中射线时响应）
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _currentRadius = Mathf.Clamp(_currentRadius + scroll * 1f, minRadius, maxRadius);
        }

        // +/- 调大小（多种键充备，防止输入法吃键）
        if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.Plus))
            _currentRadius = Mathf.Min(_currentRadius + radiusStep * Time.deltaTime * 5f, maxRadius);
        if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus))
            _currentRadius = Mathf.Max(_currentRadius - radiusStep * Time.deltaTime * 5f, minRadius);

        // 射线
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            UpdatePreview(hit.point);

            // 左键放置
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !Input.GetKey(KeyCode.X))
            {
                PlaceSdfSphere(hit.point);
            }
        }
        else if (_preview != null)
        {
            _preview.SetActive(false);
        }

        // X + 左键删除最近的 SdfPiece
        if (Input.GetKey(KeyCode.X) && Input.GetMouseButtonDown(0))
        {
            DeleteNearest(ray);
        }
    }

    void PlaceSdfSphere(Vector3 pos)
    {
        // 将位置 clamp 到 SDF 体积范围
        if (_volume != null)
        {
            Vector3 vCenter = _volume.transform.position;
            Vector3 vSize = _volume.size;
            Vector3 vMin = vCenter - vSize * 0.5f;
            Vector3 vMax = vCenter + vSize * 0.5f;
            float r = _currentRadius;
            Vector3 clamped;
            clamped.x = Mathf.Clamp(pos.x, vMin.x + r, vMax.x - r);
            clamped.y = Mathf.Clamp(pos.y, vMin.y + r, vMax.y - r);
            clamped.z = Mathf.Clamp(pos.z, vMin.z + r, vMax.z - r);
            Debug.Log($"[SdfPlacer] raw={pos}, clamped={clamped}, vol center={vCenter}, vol size={vSize}");
            pos = clamped;
        }

        var go = new GameObject("SdfSphere");
        go.transform.position = pos;
        var piece = go.AddComponent<SdfPiece>();
        piece.shape = SdfPiece.ShapeType.Sphere;
        piece.radius = _currentRadius;
        piece.RegisterToVolume();

        // 同步让沙地隆起
        if (_terrain != null)
        {
            _terrain.Pile(pos, _currentRadius * 1.5f, _currentRadius * 0.4f);
        }
    }

    void DeleteNearest(Ray ray)
    {
        // 找离射线最近的 SdfPiece
        SdfPiece nearest = null;
        float minDist = float.MaxValue;
        foreach (var p in FindObjectsOfType<SdfPiece>())
        {
            float d = Vector3.Cross(ray.direction, p.transform.position - ray.origin).magnitude;
            if (d < minDist)
            {
                minDist = d;
                nearest = p;
            }
        }
        if (nearest != null && minDist < 0.05f)
        {
            Destroy(nearest.gameObject);
        }
    }

    void CreatePreview()
    {
        _preview = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _preview.name = "SdfPreview";
        Destroy(_preview.GetComponent<Collider>());
        var r = _preview.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", new Color(0.9f, 0.8f, 0.6f, 0.3f));
        mat.SetFloat("_Surface", 1);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        r.sharedMaterial = mat;
    }

    void UpdatePreview(Vector3 pos)
    {
        if (_preview == null) return;
        _preview.SetActive(true);
        _preview.transform.position = pos;
        _preview.transform.localScale = Vector3.one * _currentRadius * 2f;
    }
}
