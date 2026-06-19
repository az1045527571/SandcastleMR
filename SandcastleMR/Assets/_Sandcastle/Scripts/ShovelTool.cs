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

        [Header("铲动动画（按住抬起 / 松开回落）")]
        [Tooltip("抬起量随时间的过渡曲线（0~1）。用于按下/松开的平滑感")]
        public AnimationCurve liftCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        [Tooltip("按住时铲子抬起的角度（绕 Z，正负决定方向）")]
        public float liftAngle = -55f;
        [Tooltip("抬起/回落的速度（越大越快）")]
        public float liftSpeed = 6f;
        [Tooltip("抬起时向上抬高度（米）")]
        public float liftUp = 0.08f;

        [Header("事件（接音效/粒子）")]
        public UnityEvent OnDig;   // 挖的瞬间
        public UnityEvent OnDrop;  // 倒的瞬间

        public bool IsActive { get; private set; }
        public bool IsLoaded { get; private set; }

        /// <summary>全局：铲子模式是否激活。其他放置器据此让出鼠标（与盖碍堡解耦）。</summary>
        public static bool ShovelActive { get; private set; }

        private Camera _cam;

        // 抬起动画状态（0=默认角度, 1=完全抬起）
        private float _lift;
        private bool _holding;
        private SdfVolume _volume;
        private GameObject _shovel;
        private Renderer[] _shovelRenderers;
        private Transform _digPoint;
        private GameObject _loadedSand;

        void Start()
        {
            _cam = Camera.main;
            _volume = FindObjectOfType<SdfVolume>();

            // 优先：如果本物体本身就是铲子模型（含 cutbox 子物体），直接用自己
            var selfDig = FindChild(transform, digPointName);
            if (selfDig != null)
            {
                _shovel = gameObject;
                _digPoint = selfDig;
                var s = FindChild(transform, loadedSandName);
                if (s != null) _loadedSand = s.gameObject;
            }
            else
            {
                // 否则实例化 prefab
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
            bool hit = _volume.RaycastSandOrPhysics(ray, out Vector3 hitPos);  // SDF 求交优先, 不依赖每帧 collider

            // 抬起量：按住朝 1 过渡，松开朝 0 过渡
            float target = _holding ? 1f : 0f;
            _lift = Mathf.MoveTowards(_lift, target, liftSpeed * Time.deltaTime);
            float liftEval = liftCurve.Evaluate(_lift);
            float tiltZ = liftEval * liftAngle;
            float upY = liftEval * liftUp;

            // 铲子跟随鼠标：面向相机 + 基础姿态 + 抬起偏移，让 cutbox 对准命中点
            if (hit && _shovel != null)
            {
                SetShovelVisible(true);
                Vector3 toCam = _cam.transform.position - hitPos;
                toCam.y = 0;
                Quaternion faceRot = (toCam.sqrMagnitude > 1e-4f)
                    ? Quaternion.LookRotation(toCam.normalized, Vector3.up)
                    : Quaternion.identity;
                _shovel.transform.rotation = faceRot * Quaternion.Euler(baseEuler.x, baseEuler.y, baseEuler.z + tiltZ);
                _shovel.transform.position = hitPos;
                if (_digPoint != null)
                {
                    Vector3 offset = _digPoint.position - _shovel.transform.position;
                    _shovel.transform.position = hitPos - offset;
                }
                // 抬起时整体抬高
                _shovel.transform.position += Vector3.up * upY;
            }
            else if (_shovel != null)
            {
                SetShovelVisible(false);
            }

            // 笔刷中心 = cutbox 世界位置（没有则用命中点）
            Vector3 center = (_digPoint != null) ? _digPoint.position : hitPos;
            float rotY = _shovel != null ? _shovel.transform.eulerAngles.y : 0f;

            // 按住左键：铲子抬起；空铲按下瞬间挖
            if (hit && Input.GetMouseButtonDown(0) && !IsLoaded)
            {
                _holding = true;
                Dig(center, rotY);
            }
            // 满铲按住也抬起（拿着沙）
            else if (Input.GetMouseButtonDown(0) && IsLoaded)
            {
                _holding = true;
            }

            // 松开左键：铲子回落默认角度；满铲则倒出
            if (Input.GetMouseButtonUp(0))
            {
                _holding = false;
                if (IsLoaded)
                {
                    Vector3 dropCenter = (_digPoint != null) ? _digPoint.position : center;
                    Drop(dropCenter, rotY);
                }
            }
        }

        void Dig(Vector3 center, float rotationY)
        {
            int n = _volume.BoxBrush(center, boxHalfExtents, rotationY, dig: true, brushAmount);
            _volume.RebuildMesh();
            IsLoaded = true;
            UpdateLoadedVisual();
            OnDig?.Invoke();
        }

        void Drop(Vector3 center, float rotationY)
        {
            _volume.BoxBrush(center, boxHalfExtents, rotationY, dig: false, brushAmount);
            _volume.RebuildMesh();
            IsLoaded = false;
            UpdateLoadedVisual();
            OnDrop?.Invoke();
        }

        void SetActive(bool on)
        {
            IsActive = on;
            ShovelActive = on;
            SetShovelVisible(on);
        }

        // 切换铲子可见性用渲染器开关，而不是禁用整个物体
        // （ShovelTool 就挂在铲子根上，禁用物体会连 Update 一起停，按 T 就失灵）
        void SetShovelVisible(bool on)
        {
            if (_shovel == null) return;
            if (_shovelRenderers == null)
                _shovelRenderers = _shovel.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _shovelRenderers)
                if (r != null) r.enabled = on;
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
