using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 脚印（沙面法线贴花的一个客户端）。
    /// 渲染走通用的 SandDecalSystem，这里只负责"走路时盖左右脚贴花"。
    /// 按住 F，鼠标在沙面移动，沿方向留下左右交替的脚印，随时间淡出。
    /// </summary>
    public class FootprintManager : MonoBehaviour
    {
        [Header("脚印")]
        [Tooltip("脚印半尺寸（米，正方形不拉伸）")]
        public float size = 0.18f;
        [Tooltip("法线强度")]
        public float normalStrength = 2.5f;
        [Tooltip("反照率压暗（0~1，踩实变深）")]
        public float darken = 0.3f;
        [Tooltip("左右脚横向间距（米）")]
        public float stride = 0.12f;
        [Tooltip("每走多远留一个脚印（米）")]
        public float stepDistance = 0.25f;
        [Tooltip("脚印存活时间（秒）")]
        public float lifetime = 8f;

        private Camera _cam;
        private int _layerR = -1, _layerL = -1;
        private Vector2 _lastXZ;
        private bool _hasLast;
        private bool _leftFoot;

        void Start()
        {
            _cam = Camera.main;
            var texR = Resources.Load<Texture2D>("footprint_R_normal");
            var texL = Resources.Load<Texture2D>("footprint_L_normal");
            var sys = SandDecalSystem.Instance;
            if (sys == null)
            {
                Debug.LogWarning("[Footprint] 未找到 SandDecalSystem");
                enabled = false;
                return;
            }
            if (texR != null) _layerR = sys.RegisterTexture(texR);
            if (texL != null) _layerL = sys.RegisterTexture(texL);
            if (texR == null || texL == null)
                Debug.LogWarning("[Footprint] 未找到 Resources/footprint_R_normal 或 _L_normal");
        }

        void Update()
        {
            if (_cam == null) return;

            if (Input.GetKey(KeyCode.F))
            {
                Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                {
                    Vector2 xz = new Vector2(hit.point.x, hit.point.z);
                    if (!_hasLast)
                    {
                        _lastXZ = xz;
                        _hasLast = true;
                    }
                    else
                    {
                        Vector2 delta = xz - _lastXZ;
                        if (delta.magnitude >= stepDistance)
                        {
                            Vector2 dir = delta.normalized;
                            float yaw = Mathf.Atan2(dir.x, dir.y);
                            Vector2 side = new Vector2(dir.y, -dir.x) * (stride * 0.5f) * (_leftFoot ? 1f : -1f);
                            int layer = _leftFoot ? _layerL : _layerR;
                            if (layer >= 0)
                                SandDecalSystem.Instance.AddDecal(xz + side, yaw, layer,
                                    size, darken, normalStrength, lifetime);
                            _leftFoot = !_leftFoot;
                            _lastXZ = xz;
                        }
                    }
                }
            }
            else
            {
                _hasLast = false;
            }
        }
    }
}
