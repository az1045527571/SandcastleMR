using UnityEngine;

/// <summary>
/// 构件放置器：把预制城堡构件放到沙面上。
/// 
/// 操作：
///   左键单击沙面 = 放置当前选中构件
///   X + 左键    = 删除鼠标指向的构件
///   R 按住      = 旋转预览
///   + / -       = 缩放预览大小
///   1/2/3       = 切换构件类型
///
/// 需要场景中有 SandTerrain。
/// </summary>
public class PiecePlacer : MonoBehaviour
{
    [Header("可放置的构件 Prefab 列表")]
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

    void Start()
    {
        _cam = Camera.main;
        _terrain = FindObjectOfType<SandTerrain>();

        if (piecePrefabs == null || piecePrefabs.Length == 0)
        {
            piecePrefabs = new GameObject[] { CreateDefaultTower() };
        }

        CreatePreview();
    }

    void Update()
    {
        if (_cam == null) return;

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
            _currentScale = Mathf.Min(_currentScale + scaleStep, maxScale);
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            _currentScale = Mathf.Max(_currentScale - scaleStep, minScale);

        // R 旋转
        if (Input.GetKey(KeyCode.R))
            _currentRotY += rotateSpeed * Time.deltaTime;

        // 左键：放置（排除右键旋转中和 X 删除模式）
        if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !Input.GetKey(KeyCode.X))
        {
            PlacePiece();
        }

        // X + 左键 = 删除
        if (Input.GetKey(KeyCode.X) && Input.GetMouseButtonDown(0))
        {
            DeletePiece();
        }
    }

    void UpdatePreview()
    {
        if (_preview == null) return;

        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            _preview.SetActive(true);
            _preview.transform.position = hit.point;
            _preview.transform.rotation = Quaternion.Euler(0f, _currentRotY, 0f);
            _preview.transform.localScale = Vector3.one * _currentScale;
        }
        else
        {
            _preview.SetActive(false);
        }
    }

    void PlacePiece()
    {
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

        var prefab = piecePrefabs[_selectedIndex];
        var go = Instantiate(prefab);
        go.name = prefab.name + "_placed";
        go.SetActive(true);
        go.transform.position = hit.point;
        go.transform.rotation = Quaternion.Euler(0f, _currentRotY, 0f);
        go.transform.localScale = Vector3.one * _currentScale;

        // 确保有 CastlePiece 组件
        if (go.GetComponent<CastlePiece>() == null)
            go.AddComponent<CastlePiece>();

        // 确保有 Collider 用于删除射线
        if (go.GetComponent<Collider>() == null)
        {
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.6f, 0f);
            col.radius = 0.35f;
            col.height = 1.2f;
        }

        // 设置不透明材质，并传入脚下沙面高度
        SetMaterialOpaque(go);
        SetPieceBaseY(go, hit.point.y);

        // 在塔脚下堆出沙堆基座（堆高一些，盖住塔底部）
        var piece = go.GetComponent<CastlePiece>();
        if (_terrain != null && piece != null)
        {
            float radius = piece.baseRadius * _currentScale * 1.8f;
            float moundHeight = 0.35f * _currentScale;  // 堆高，盖住塔底
            _terrain.Pile(hit.point, radius, moundHeight);
            _terrain.Pile(hit.point, radius * 0.5f, moundHeight * 0.4f);

            // 塔往下沉，让沙堆把底部接缝盖住
            float sink = piece.baseSink * _currentScale + 0.08f * _currentScale;
            go.transform.position = hit.point - new Vector3(0f, sink, 0f);
        }
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
        _preview.SetActive(true);

        // 移除 Collider（预览不参与物理）
        foreach (var col in _preview.GetComponentsInChildren<Collider>())
            Destroy(col);

        // 半透明
        SetMaterialTransparent(_preview);
    }

    void SetMaterialTransparent(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = 0.4f;
                    mat.SetColor("_BaseColor", c);
                }
                mat.SetFloat("_Surface", 1);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
            }
        }
    }

    void SetPieceBaseY(GameObject go, float y)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_PieceBaseY"))
                    mat.SetFloat("_PieceBaseY", y);
            }
        }
    }

    void SetMaterialOpaque(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = 1f;
                    mat.SetColor("_BaseColor", c);
                }
                mat.SetFloat("_Surface", 0);
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.renderQueue = -1;
            }
        }
    }

    GameObject CreateDefaultTower()
    {
        var tower = new GameObject("RoundTower");
        tower.SetActive(false);

        // 塔身
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Body";
        body.transform.SetParent(tower.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        body.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);

        // 塔顶
        var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        top.name = "Top";
        top.transform.SetParent(tower.transform, false);
        top.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        top.transform.localScale = new Vector3(0.7f, 0.3f, 0.7f);

        // 材质：使用 CastlePiece shader（与 Sand 同质，仅额外做交界处法线融合）
        Shader shader = Shader.Find("Sandcastle/CastlePiece");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        body.GetComponent<Renderer>().sharedMaterial = mat;
        top.GetComponent<Renderer>().sharedMaterial = mat;

        // CastlePiece
        var piece = tower.AddComponent<CastlePiece>();
        piece.baseRadius = 0.35f;

        // 碰撞体
        var col = tower.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.6f, 0f);
        col.radius = 0.35f;
        col.height = 1.2f;

        DontDestroyOnLoad(tower);
        return tower;
    }
}
