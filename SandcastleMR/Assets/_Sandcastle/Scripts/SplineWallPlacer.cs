using System.Collections.Generic;
using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 样条沙堤放置器：
    /// - 按 G 进入/退出样条绘制模式
    /// - 左键：在鼠标命中点添加一个控制点
    /// - 回车 / 右键：完成，生成一道固定高度的沙堤（注册为 Spline 形状的 SdfPiece）
    /// - Backspace：撤销上一个控制点
    /// - Esc：取消当前绘制
    ///
    /// 沙堤沿控制点折线、固定半宽、堤顶为沙面上方 wallHeight 米的实心沙。
    /// </summary>
    public class SplineWallPlacer : MonoBehaviour
    {
        [Header("沙堤参数")]
        [Tooltip("堤的半宽（米）")]
        public float wallRadius = 0.3f;
        [Tooltip("堤顶高出沙面多少米")]
        public float wallHeight = 0.4f;

        [Header("预览")]
        public Color previewColor = new Color(0.2f, 0.9f, 1f, 1f);
        public float markerSize = 0.1f;

        private Camera _cam;
        private SdfVolume _volume;
        private bool _drawing;

        /// <summary>全局：是否有样条正在绘制。其他放置器据此让出鼠标。</summary>
        public static bool IsDrawing { get; private set; }
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<GameObject> _markers = new List<GameObject>();
        private LineRenderer _previewLine;

        void Start()
        {
            _cam = Camera.main;
            _volume = FindObjectOfType<SdfVolume>();
        }

        void Update()
        {
            if (_cam == null || _volume == null) return;

            // G 切换绘制模式
            if (Input.GetKeyDown(KeyCode.G))
            {
                if (_drawing) CancelDrawing();
                else StartDrawing();
            }

            if (!_drawing) return;

            // 左键加点
            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.X))
            {
                Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                    AddPoint(hit.point);
            }

            // Backspace 撤销
            if (Input.GetKeyDown(KeyCode.Backspace) && _points.Count > 0)
            {
                _points.RemoveAt(_points.Count - 1);
                Destroy(_markers[_markers.Count - 1]);
                _markers.RemoveAt(_markers.Count - 1);
                UpdatePreviewLine();
            }

            // 回车 / 右键 完成
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(1))
                FinishDrawing();

            // Esc 取消
            if (Input.GetKeyDown(KeyCode.Escape))
                CancelDrawing();
        }

        void StartDrawing()
        {
            _drawing = true;
            IsDrawing = true;
            _points.Clear();
            var lineGo = new GameObject("SplinePreviewLine");
            lineGo.transform.SetParent(transform, false);
            _previewLine = lineGo.AddComponent<LineRenderer>();
            _previewLine.useWorldSpace = true;
            _previewLine.widthMultiplier = wallRadius * 2f;
            _previewLine.positionCount = 0;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", previewColor);
            _previewLine.material = mat;
            _previewLine.startColor = _previewLine.endColor = previewColor;
            Debug.Log("[SplineWall] 开始绘制：左键加点，回车/右键完成，Backspace撤销，Esc取消");
        }

        void AddPoint(Vector3 worldPos)
        {
            // 吸到沙面高度，控制点 Y 用沙面世界 Y
            worldPos.y = _volume.SandSurfaceWorldY;
            _points.Add(worldPos);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "SplineMarker";
            marker.transform.position = worldPos;
            marker.transform.localScale = Vector3.one * markerSize;
            foreach (var col in marker.GetComponentsInChildren<Collider>())
                Destroy(col);
            var mr = marker.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", previewColor);
            mr.sharedMaterial = mat;
            _markers.Add(marker);

            UpdatePreviewLine();
        }

        void UpdatePreviewLine()
        {
            if (_previewLine == null) return;
            _previewLine.positionCount = _points.Count;
            for (int i = 0; i < _points.Count; i++)
                _previewLine.SetPosition(i, _points[i] + Vector3.up * 0.01f);
        }

        void FinishDrawing()
        {
            if (_points.Count < 1)
            {
                CancelDrawing();
                return;
            }

            float sandY = _volume.SandSurfaceWorldY;
            var go = new GameObject("SdfSplineWall");
            go.transform.position = Vector3.zero; // spline 用世界坐标点，物体放原点即可
            var piece = go.AddComponent<SdfPiece>();
            piece.shape = SdfPiece.ShapeType.Spline;
            piece.splinePoints = new List<Vector3>(_points);
            piece.splineRadius = wallRadius;
            piece.splineTopY = sandY + wallHeight;
            piece.splineBottomY = sandY - 0.5f; // 向下扎进沙层确保与沙堆相连
            piece.RegisterToVolume();

            Debug.Log($"[SplineWall] 完成：{_points.Count} 个控制点，堤顶 Y={sandY + wallHeight:F3}");

            CleanupPreview();
            _drawing = false;
            IsDrawing = false;
        }

        void CancelDrawing()
        {
            CleanupPreview();
            _drawing = false;
            IsDrawing = false;
            Debug.Log("[SplineWall] 取消绘制");
        }

        void CleanupPreview()
        {
            foreach (var m in _markers) if (m != null) Destroy(m);
            _markers.Clear();
            _points.Clear();
            if (_previewLine != null) Destroy(_previewLine.gameObject);
            _previewLine = null;
        }
    }
}
