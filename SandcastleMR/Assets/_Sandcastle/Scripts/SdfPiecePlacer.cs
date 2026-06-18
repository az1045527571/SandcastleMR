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
    public float defaultRadius = 0.3f;
    public float minRadius = 0.05f;
    public float maxRadius = 1f;
    public float radiusStep = 0.05f;

    private Camera _cam;
    private SdfVolume _volume;
    private float _currentRadius;
    private GameObject _preview;

    void Start()
    {
        _cam = Camera.main;
        _volume = FindObjectOfType<SdfVolume>();
        _currentRadius = defaultRadius;
        CreatePreview();
    }

    void Update()
    {
        if (_cam == null || _volume == null) return;
        if (!enabled) return;

        // +/- 调大小
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            _currentRadius = Mathf.Min(_currentRadius + radiusStep, maxRadius);
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            _currentRadius = Mathf.Max(_currentRadius - radiusStep, minRadius);

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
        var go = new GameObject("SdfSphere");
        go.transform.position = pos;
        var piece = go.AddComponent<SdfPiece>();
        piece.shape = SdfPiece.ShapeType.Sphere;
        piece.radius = _currentRadius;
        // SdfPiece.OnEnable 会自动注册到 SdfVolume 并触发重建
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
