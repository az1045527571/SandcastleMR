using UnityEngine;
using UnityEngine.Events;

namespace Sandcastle
{
    /// <summary>
    /// 铲子工具：固定 Box 区域挖一铲沙、搬到另一处倒出。纯体素操作，不真模拟。
    ///
    /// 状态机（严格守恒）：
    ///   空铲 → 左键点沙面 = 挖（Box 区域削掉，沙面凹下方坑）→ 变满
    ///   满铲 → 左键点沙面 = 倒（Box 区域堆起，沙面凸起）→ 变空
    ///
    /// 体积不按铲子模型真实体积算，用一个固定可调的 Box 笔刷。
    ///
    /// 视觉与反馈完全解耦：
    ///   - loadedSandMesh：满铲时显示的"载沙"假沙 mesh（自动显隐 + 也可在事件里自己控制）
    ///   - OnDig / OnDrop：挖/倒的瞬间触发，Inspector 里接音效/粒子/动画
    /// </summary>
    public class ShovelTool : MonoBehaviour
    {
        [Header("启用")]
        [Tooltip("按此键切换铲子模式")]
        public KeyCode toggleKey = KeyCode.T;
        [Tooltip("是否一开始就启用铲子模式")]
        public bool startEnabled = false;

        [Header("Box 笔刷（一铲的范围，米）")]
        [Tooltip("半边长。实际挖填区域是 2×这个值的盒子")]
        public Vector3 boxHalfExtents = new Vector3(0.15f, 0.12f, 0.15f);
        [Tooltip("挖/填的 SDF 偏移量（越大削/堆越狠）")]
        public float brushAmount = 0.6f;

        [Header("铲子模型")]
        [Tooltip("铲子 prefab（CHANZIGONGJU）。留空则运行时从 Resources 加载")]
        public GameObject shovelPrefab;
        [Tooltip("铲斗中心/鼠标对齐点子物体名（笔刷落点）。找不到则用模型原点")]
        public string digPointName = "cutbox";
        [Tooltip("载沙假沙 mesh 子物体名（满铲时显示）")]
        public string loadedSandName = "CHANZI_SHAZI";

        [Header("姿态")]
        [Tooltip("铲子初始/基础姿态欧拉角（在面向相机的基础上叠加）。x=俯仰/倾斜, y=纵向翻转, z=滚转")]
        public Vector3 baseEuler = new Vector3(45f, 180f, 0f);

        [Header("铲动动画")]
        [Tooltip("挬动强度随时间的曲线（0~1 时间 → 0~1 强度）。所有挬动/俱冲都乘这个值")]
        public AnimationCurve swingCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
        [Tooltip("铲/招时绕 Z 挬动的峰值角度（正负决定挬动方向）")]
        public float swingAngle = -55f;
        [Tooltip("挬动单程时长（秒）")]
        public float swingDuration = 0.28f;
        [Tooltip("俱冲向下深度（米）")]
        public float plungeDown = 0.12f;
        [Tooltip("俱冲向前距离（米，沿铲子朝向）")]
        public float plungeForward = 0.1f;

        [Header("事件（接音效/粒子）")]
        public UnityEvent OnDig;   // 挖的瞬间
        public UnityEvent OnDrop;  // 倒的瞬间

        public bool IsActive { get; private set; }
        public bool IsLoaded { get; private set; }

        /// <summary>全局：铲子模式是否激活。其他放置器据此让出鼠标（与盖碍堡解耦）。</summary>
        public static bool ShovelActive { get; private set; }

        private Camera _cam;

        // 挬动动画状态
        private bool _swinging;
        private float _swingT;
        private Quaternion _swingFaceRot;   // 挬动期间锁定的面向机朝向
        private Vector3 _swingPos;          // 挬动期间锁定的位置
        private SdfVolume _volume;
        private GameObject _shovel;
        private Transform _digPoint;
        private GameObject _loadedSand;

        void Start()
        {
            _cam = Camera.main;
            _volume = FindObjectOfType<SdfVolume>();

            if (shovelPrefab == null)
                shovelPrefab = Resources.Load<GameObject>("CHANZIGONGJU");
            if (shovelPrefab != null)
            {
                _shovel = Instantiate(shovelPrefab);
                _shovel.name = "ShovelInstance";
                _digPoint = FindChild(_shovel.transform, digPointName);
                var sand = FindChild(_shovel.transform, loadedSandName);
                if (sand != null) _loadedSand = sand.gameObject;
            }
            else
            {
                Debug.LogWarning("[Shovel] 未找到铲子 prefab（Resources/CHANZIGONGJU 或 Inspector 指定）");
            }

            SetActive(startEnabled);
            UpdateLoadedVisual();
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetActive(!IsActive);

            if (!IsActive || _cam == null || _volume == null) return;

            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            bool hit = Physics.Raycast(ray, out RaycastHit rh, 100f);

            // 挬动动画期间：锁定位置/面向，只叠加绕 Z 的挬动，不响应跟随
            if (_swinging)
            {
                _swingT += Time.deltaTime;
                float u = Mathf.Clamp01(_swingT / Mathf.Max(swingDuration, 1e-3f));
                float arc = swingCurve.Evaluate(u); // 可在 Inspector 调的曲线
                float swingZ = arc * swingAngle;
                if (_shovel != null)
                {
                    _shovel.transform.rotation = _swingFaceRot * Quaternion.Euler(baseEuler.x, baseEuler.y, baseEuler.z + swingZ);
                    // 位置弧线：沿铲子朝向向前 + 向下俱冲再抬起
                    Vector3 fwd = _swingFaceRot * Vector3.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize();
                    Vector3 plunge = fwd * (plungeForward * arc) + Vector3.down * (plungeDown * arc);
                    _shovel.transform.position = _swingPos + plunge;
                }
                if (u >= 1f) _swinging = false;
                return;
            }

            // 铲子跟随鼠标：面向相机 + 基础姿态，让 cutbox 对准命中点
            if (hit && _shovel != null)
            {
                _shovel.SetActive(true);
                Vector3 toCam = _cam.transform.position - rh.point;
                toCam.y = 0;
                Quaternion faceRot = (toCam.sqrMagnitude > 1e-4f)
                    ? Quaternion.LookRotation(toCam.normalized, Vector3.up)
                    : Quaternion.identity;
                _shovel.transform.rotation = faceRot * Quaternion.Euler(baseEuler);
                _shovel.transform.position = rh.point;
                if (_digPoint != null)
                {
                    Vector3 offset = _digPoint.position - _shovel.transform.position;
                    _shovel.transform.position = rh.point - offset;
                }
            }
            else if (_shovel != null)
            {
                _shovel.SetActive(false);
            }

            // 笔刷中心 = cutbox 世界位置（没有则用命中点）
            Vector3 center = (_digPoint != null) ? _digPoint.position : rh.point;
            float rotY = _shovel != null ? _shovel.transform.eulerAngles.y : 0f;

            // 按住左键铲（空铲才能挖），松开招（满铲才能倒）
            if (hit && Input.GetMouseButtonDown(0) && !IsLoaded)
                Dig(center, rotY);

            if (Input.GetMouseButtonUp(0) && IsLoaded)
            {
                Vector3 dropCenter = (_digPoint != null) ? _digPoint.position : center;
                Drop(dropCenter, rotY);
            }
        }

        /// <summary>触发一次绕 Z 的铲动挬动动画（锁定当前位置/面向）。</summary>
        void StartSwing()
        {
            if (_shovel == null) return;
            _swinging = true;
            _swingT = 0f;
            _swingPos = _shovel.transform.position;
            // 从当前总朝向反推出面向机部分（去掉 baseEuler），供挬动期间叠加
            _swingFaceRot = _shovel.transform.rotation * Quaternion.Inverse(Quaternion.Euler(baseEuler));
        }

        void Dig(Vector3 center, float rotationY)
        {
            int n = _volume.BoxBrush(center, boxHalfExtents, rotationY, dig: true, brushAmount);
            _volume.RebuildMesh();
            IsLoaded = true;
            UpdateLoadedVisual();
            StartSwing();
            OnDig?.Invoke();
        }

        void Drop(Vector3 center, float rotationY)
        {
            _volume.BoxBrush(center, boxHalfExtents, rotationY, dig: false, brushAmount);
            _volume.RebuildMesh();
            IsLoaded = false;
            UpdateLoadedVisual();
            StartSwing();
            OnDrop?.Invoke();
        }

        void SetActive(bool on)
        {
            IsActive = on;
            ShovelActive = on;
            if (_shovel != null) _shovel.SetActive(on);
        }

        void UpdateLoadedVisual()
        {
            // 兜底显隐：满铲显示载沙 mesh，空铲隐藏。你也可在 OnDig/OnDrop 里自己控制。
            if (_loadedSand != null) _loadedSand.SetActive(IsLoaded);
        }

        static Transform FindChild(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
