using UnityEngine;

/// <summary>
/// 构件放置器：鼠标交互把构件放到沙面上。
/// 
/// 操作：
///   T + 左键 = 在鼠标位置放置当前选中构件
///   X + 左键 = 删除鼠标指向的构件
///   R（按住拖动） = 旋转预览
///   1/2/3 = 切换构件类型（按 PiecePrefabs 索引）
///   +/- = 缩放预览大小
///
/// 需要场景中有 SandTerrain。
/// </summary>
public class PiecePlacer : MonoBehaviour
{
    [Header("可放置的构件 Prefab 列表")]
    [Tooltip("为空时会自动生成一个默认圆塔")]
    public GameObject[] piecePrefabs;

    [Header("放置参数")]
    public float minScale = 0.3f;
    public float maxScale = 3f;
    public float scaleStep = 0.1f;
    public float rotateSpeed = 120f;

    private int _selectedIndex = 0;
    private float _currentScale = 1f;
    private float _currentRotY = 0f;
    private GameObject _preview;
    private Camera _cam;
    private SandTerrain _terrain;
    private bool _isPlacing;

    void Start()
    {
        _cam = Camera.main;
        _terrain = FindObjectOfType<SandTerrain>();

        // 如果没有配置 prefab，自动创建默认圆塔
        if (piecePrefabs == null || piecePrefabs.Length == 0)
        {
            piecePrefabs = new GameObject[] { CreateDefaultTower() };
        }

        CreatePreview();
    }

    void Update()
    {
        if (_cam == null || _terrain == null) return;

        HandleInput();
        UpdatePreview();
    }

    void HandleInput()
    {
        // 数字键切换构件
        for (int i = 0; i < Mathf.Min(9, piecePrefabs.Length); i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                _selectedIndex = i;
                CreatePreview();
            }
        }

        // +/- 缩放
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            _currentScale = Mathf.Min(_currentScale + scaleStep, maxScale);
        }
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            _currentScale = Mathf.Max(_currentScale - scaleStep, minScale);
        }

        // R 旋转
        if (Input.GetKey(KeyCode.R))
        {
            _currentRotY += rotateSpeed * Time.deltaTime;
        }

        // T + 左键 = 放置
        if (Input.GetKey(KeyCode.T) && Input.GetMouseButtonDown(0))
        {
            PlacePiece();
        }

        // X + 左键 = 删除
        if (Input.GetKey(KeyCode.X) && Input.GetMouseButtonDown(0))
        {
            DeletePiece();
        }

        // 进入放置模式时显示预览
        _isPlacing = Input.GetKey(KeyCode.T);
        if (_preview != null)
        {
            _preview.SetActive(_isPlacing);
        }
    }

    void UpdatePreview()
    {
        if (_preview == null || !_isPlacing) return;

        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            _preview.transform.position = hit.point;
            _preview.transform.rotation = Quaternion.Euler(0f, _currentRotY, 0f);
            _preview.transform.localScale = Vector3.one * _currentScale;
        }
    }

    void PlacePiece()
    {
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

        var prefab = piecePrefabs[_selectedIndex];
        var go = Instantiate(prefab);
        go.name = prefab.name + "_placed";
        go.transform.position = hit.point;
        go.transform.rotation = Quaternion.Euler(0f, _currentRotY, 0f);
        go.transform.localScale = Vector3.one * _currentScale;

        // 确保有 CastlePiece 组件
        if (go.GetComponent<CastlePiece>() == null)
            go.AddComponent<CastlePiece>();

        // 确保有 Collider 用于之后的删除射线
        if (go.GetComponent<Collider>() == null)
            go.AddComponent<MeshCollider>();

        // 移除半透明预览材质，恢复实体
        SetMaterialOpaque(go);
    }

    void DeletePiece()
    {
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

        var piece = hit.collider.GetComponent<CastlePiece>();
        if (piece == null)
            piece = hit.collider.GetComponentInParent<CastlePiece>();
        if (piece != null)
            Destroy(piece.gameObject);
    }

    void CreatePreview()
    {
        if (_preview != null) Destroy(_preview);

        var prefab = piecePrefabs[_selectedIndex];
        _preview = Instantiate(prefab);
        _preview.name = "PiecePreview";
        _preview.SetActive(false);

        // 移除 Collider（预览不参与物理）
        foreach (var col in _preview.GetComponentsInChildren<Collider>())
            Destroy(col);

        // 半透明材质表示预览
        SetMaterialTransparent(_preview);
    }

    void SetMaterialTransparent(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.materials)
            {
                Color c = mat.color;
                c.a = 0.4f;
                mat.color = c;
                mat.SetFloat("_Surface", 1);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
            }
        }
    }

    void SetMaterialOpaque(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.materials)
            {
                Color c = mat.color;
                c.a = 1f;
                mat.color = c;
                mat.SetFloat("_Surface", 0);
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.renderQueue = -1;
            }
        }
    }

    /// <summary>
    /// 程序化生成默认圆塔 prefab（Cylinder + 顶部锥形）
    /// </summary>
    GameObject CreateDefaultTower()
    {
        var tower = new GameObject("RoundTower");
        tower.SetActive(false);

        // 塔身（圆柱）
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Body";
        body.transform.SetParent(tower.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        body.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);

        // 塔顶（缩放球体当圆锥占位）
        var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        top.name = "Top";
        top.transform.SetParent(tower.transform, false);
        top.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        top.transform.localScale = new Vector3(0.7f, 0.3f, 0.7f);

        // 统一沙色材质
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", new Color(0.88f, 0.78f, 0.58f));
        mat.SetFloat("_Smoothness", 0.15f);
        body.GetComponent<Renderer>().sharedMaterial = mat;
        top.GetComponent<Renderer>().sharedMaterial = mat;

        // 添加 CastlePiece
        var piece = tower.AddComponent<CastlePiece>();
        piece.baseRadius = 0.35f;

        // 添加一个整体碰撞体（用于删除射线检测）
        var col = tower.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.6f, 0f);
        col.radius = 0.35f;
        col.height = 1.2f;

        tower.SetActive(true);
        DontDestroyOnLoad(tower);
        tower.SetActive(false);
        return tower;
    }
}
