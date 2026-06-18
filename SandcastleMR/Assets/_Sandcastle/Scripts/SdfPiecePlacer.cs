using UnityEngine;
using Sandcastle;

/// <summary>
/// SDF 模式的构件放置器。
/// 按 2 切换到此模式（原来的按 1 保留旧系统）。
/// 
/// 形状切换：
///   B = 球（Sphere）
///   M = 烘焙 mesh（BakedMesh，使用 bakedSdfList 的资产）
/// 
/// 操作：
///   左键 = 放置
///   X+左键 = 删除最近的
///   滚轮 = 调缩放
///   R = 旋转预览（仅 BakedMesh 模式）
/// </summary>
public class SdfPiecePlacer : MonoBehaviour
{
    [Header("Sphere 模式参数")]
    public float defaultRadius = 0.5f;
    public float minRadius = 0.05f;
    public float maxRadius = 2f;

    [Header("BakedMesh 模式参数")]
    public MeshSdfAsset[] bakedSdfList;
    [Tooltip("可选：每个 baked SDF 对应的视觉 prefab（如果留空则用 SDF 包围盒占位）")]
    public GameObject[] previewPrefabs;
    public float defaultBakedScale = 1f;
    public float minBakedScale = 0.2f;
    public float maxBakedScale = 3f;

    public float rotateSpeed = 120f;

    private enum Mode { Sphere, Baked }
    private Mode _mode = Mode.Sphere;
    private int _bakedIndex = 0;

    private Camera _cam;
    private SdfVolume _volume;
    private SandTerrain _terrain;
    private float _currentRadius;
    private float _currentBakedScale;
    private float _currentRotY;
    private GameObject _preview;

    void Start()
    {
        _cam = Camera.main;
        _volume = FindObjectOfType<SdfVolume>();
        _terrain = FindObjectOfType<SandTerrain>();
        _currentRadius = defaultRadius;
        _currentBakedScale = defaultBakedScale;

        // 自动加载 Resources 里的 MeshSdfAsset
        if (bakedSdfList == null || bakedSdfList.Length == 0)
        {
            bakedSdfList = Resources.LoadAll<MeshSdfAsset>("");
            if (bakedSdfList.Length > 0)
                Debug.Log($"[SdfPiecePlacer] Auto-loaded {bakedSdfList.Length} MeshSdfAsset(s) from Resources");
        }

        CreatePreview();
    }

    void Update()
    {
        if (_cam == null || _volume == null) return;
        if (!enabled) return;

        // 模式切换
        if (Input.GetKeyDown(KeyCode.B))
        {
            _mode = Mode.Sphere;
            CreatePreview();
        }
        if (Input.GetKeyDown(KeyCode.M))
        {
            _mode = Mode.Baked;
            CreatePreview();
        }
        // 数字键切 baked 索引（仅 Baked 模式）
        if (_mode == Mode.Baked && bakedSdfList != null)
        {
            for (int i = 0; i < Mathf.Min(9, bakedSdfList.Length); i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    _bakedIndex = i;
                    CreatePreview();
                }
            }
        }

        // 滚轮调大小
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            if (_mode == Mode.Sphere)
                _currentRadius = Mathf.Clamp(_currentRadius + scroll, minRadius, maxRadius);
            else
                _currentBakedScale = Mathf.Clamp(_currentBakedScale + scroll * 2f, minBakedScale, maxBakedScale);
        }

        // R 旋转
        if (Input.GetKey(KeyCode.R))
            _currentRotY += rotateSpeed * Time.deltaTime;

        // 射线
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            UpdatePreview(hit.point);
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !Input.GetKey(KeyCode.X))
            {
                Place(hit.point);
            }
        }
        else if (_preview != null)
        {
            _preview.SetActive(false);
        }

        if (Input.GetKey(KeyCode.X) && Input.GetMouseButtonDown(0))
        {
            DeleteNearest(ray);
        }
    }

    void Place(Vector3 pos)
    {
        // clamp 到 SDF 体积
        if (_volume != null)
        {
            Vector3 vCenter = _volume.transform.position;
            Vector3 vSize = _volume.size;
            Vector3 vMin = vCenter - vSize * 0.5f;
            Vector3 vMax = vCenter + vSize * 0.5f;
            float r = (_mode == Mode.Sphere) ? _currentRadius : 0.3f;
            pos.x = Mathf.Clamp(pos.x, vMin.x + r, vMax.x - r);
            pos.y = Mathf.Clamp(pos.y, vMin.y + r, vMax.y - r);
            pos.z = Mathf.Clamp(pos.z, vMin.z + r, vMax.z - r);
        }

        var go = new GameObject(_mode == Mode.Sphere ? "SdfSphere" : "SdfBaked");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0, _currentRotY, 0);

        var piece = go.AddComponent<SdfPiece>();
        if (_mode == Mode.Sphere)
        {
            piece.shape = SdfPiece.ShapeType.Sphere;
            piece.radius = _currentRadius;
            go.transform.localScale = Vector3.one;
        }
        else
        {
            piece.shape = SdfPiece.ShapeType.BakedMesh;
            piece.bakedSdf = bakedSdfList[_bakedIndex];
            go.transform.localScale = Vector3.one * _currentBakedScale;
        }
        piece.RegisterToVolume();

        if (_terrain != null)
        {
            float pileR = _mode == Mode.Sphere ? _currentRadius * 1.5f : _currentBakedScale * 0.5f;
            float pileH = _mode == Mode.Sphere ? _currentRadius * 0.4f : _currentBakedScale * 0.15f;
            _terrain.Pile(pos, pileR, pileH);
        }
    }

    void DeleteNearest(Ray ray)
    {
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
        if (nearest != null && minDist < 0.5f)
            Destroy(nearest.gameObject);
    }

    void CreatePreview()
    {
        if (_preview != null) Destroy(_preview);

        if (_mode == Mode.Sphere)
        {
            _preview = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        }
        else
        {
            // Baked 模式：优先用 previewPrefabs，没有就用包围盒立方体占位
            if (previewPrefabs != null && _bakedIndex < previewPrefabs.Length && previewPrefabs[_bakedIndex] != null)
            {
                _preview = Instantiate(previewPrefabs[_bakedIndex]);
            }
            else if (bakedSdfList != null && _bakedIndex < bakedSdfList.Length && bakedSdfList[_bakedIndex] != null)
            {
                _preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _preview.transform.localScale = bakedSdfList[_bakedIndex].bounds.size;
            }
            else
            {
                _preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
            }
        }
        _preview.name = "SdfPreview";

        foreach (var col in _preview.GetComponentsInChildren<Collider>())
            Destroy(col);

        // 半透明
        foreach (var r in _preview.GetComponentsInChildren<Renderer>())
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.9f, 0.8f, 0.6f, 0.4f));
            mat.SetFloat("_Surface", 1);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            r.sharedMaterial = mat;
        }
    }

    void UpdatePreview(Vector3 pos)
    {
        if (_preview == null) return;
        _preview.SetActive(true);
        _preview.transform.position = pos;
        _preview.transform.rotation = Quaternion.Euler(0, _currentRotY, 0);
        if (_mode == Mode.Sphere)
            _preview.transform.localScale = Vector3.one * _currentRadius * 2f;
        else
        {
            // Baked: 应用尺寸
            if (bakedSdfList != null && _bakedIndex < bakedSdfList.Length && bakedSdfList[_bakedIndex] != null)
            {
                _preview.transform.localScale = bakedSdfList[_bakedIndex].bounds.size * _currentBakedScale;
            }
            else
            {
                _preview.transform.localScale = Vector3.one * _currentBakedScale;
            }
        }
    }
}
