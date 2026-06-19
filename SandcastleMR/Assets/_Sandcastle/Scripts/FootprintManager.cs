using System.Collections.Generic;
using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 脚印气氛效果（纯 shader 法线扰动，不改几何）：
    /// - 按住 F，鼠标在沙面上移动时，沿移动方向留下左右交替的脚印
    /// - 每个脚印随时间淡出，淡出后从数组移除
    /// - 最多同时 32 个（shader 上限），超出顶掉最旧的
    ///
    /// 数据每帧传给 Sand.shader 的全局 uniform 数组 _Footprints。
    /// </summary>
    public class FootprintManager : MonoBehaviour
    {
        const int MaxFootprints = 32;

        [Header("脚印形状")]
        [Tooltip("脚印长半轴（米）")]
        public float length = 0.18f;
        [Tooltip("脚印宽半轴（米）")]
        public float width = 0.08f;
        [Tooltip("法线扰动强度")]
        public float depth = 0.6f;
        [Tooltip("左右脚横向间距（米）")]
        public float stride = 0.12f;

        [Header("行为")]
        [Tooltip("每走多远留一个脚印（米）")]
        public float stepDistance = 0.25f;
        [Tooltip("脚印存活时间（秒），之后淡出消失")]
        public float lifetime = 8f;

        private struct Footprint
        {
            public Vector2 pos;   // 世界 XZ
            public float yaw;     // 朝向（弧度）
            public float age;     // 已存活秒
        }

        private readonly List<Footprint> _prints = new List<Footprint>();
        private readonly Vector4[] _buf = new Vector4[MaxFootprints];
        private Camera _cam;
        private SdfVolume _volume;
        private Vector2 _lastXZ;
        private bool _hasLast;
        private bool _leftFoot;

        void Start()
        {
            _cam = Camera.main;
            _volume = FindObjectOfType<SdfVolume>();
            Shader.SetGlobalFloat("_FootprintLength", length);
            Shader.SetGlobalFloat("_FootprintWidth", width);
            Shader.SetGlobalFloat("_FootprintDepth", depth);
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
                            float yaw = Mathf.Atan2(dir.x, dir.y); // 与世界 +Z 的夹角
                            // 左右脚横向偏移（垂直于前进方向）
                            Vector2 side = new Vector2(dir.y, -dir.x) * (stride * 0.5f) * (_leftFoot ? 1f : -1f);
                            _leftFoot = !_leftFoot;
                            AddPrint(xz + side, yaw);
                            _lastXZ = xz;
                        }
                    }
                }
            }
            else
            {
                _hasLast = false;
            }

            // 老化 + 淡出
            for (int i = _prints.Count - 1; i >= 0; i--)
            {
                var p = _prints[i];
                p.age += Time.deltaTime;
                if (p.age >= lifetime) { _prints.RemoveAt(i); continue; }
                _prints[i] = p;
            }

            UploadToShader();
        }

        void AddPrint(Vector2 xz, float yaw)
        {
            if (_prints.Count >= MaxFootprints)
                _prints.RemoveAt(0); // 顶掉最旧的
            _prints.Add(new Footprint { pos = xz, yaw = yaw, age = 0f });
        }

        void UploadToShader()
        {
            int n = _prints.Count;
            for (int i = 0; i < n; i++)
            {
                var p = _prints[i];
                // 强度：随年龄线性淡出
                float strength = 1f - Mathf.Clamp01(p.age / lifetime);
                _buf[i] = new Vector4(p.pos.x, p.pos.y, p.yaw, strength);
            }
            Shader.SetGlobalVectorArray("_Footprints", _buf);
            Shader.SetGlobalInt("_FootprintCount", n);
            // 形状参数可能运行时被调，每帧同步
            Shader.SetGlobalFloat("_FootprintLength", length);
            Shader.SetGlobalFloat("_FootprintWidth", width);
            Shader.SetGlobalFloat("_FootprintDepth", depth);
        }
    }
}
