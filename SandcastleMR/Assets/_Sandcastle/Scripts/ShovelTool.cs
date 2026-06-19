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
        [Tooltip("铲斗中心子物体名（笔刷落点）。找不到则用模型原点")]
        public string digPointName = "DigPoint";
        [Tooltip("载沙假沙 mesh 子物体名（满铲时显示）")]
        public string loadedSandName = "CHANZI_SHAZI";

        [Header("事件（接音效/粒子）")]
        public UnityEvent OnDig;   // 挖的瞬间
        public UnityEvent OnDrop;  // 倒的瞬间

        public bool IsActive { get; private set; }
        public bool IsLoaded { get; private set; }

        private Camera _cam;
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

            // 铲子跟随鼠标在沙面悬停
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            bool hit = Physics.Raycast(ray, out RaycastHit rh, 100f);
            if (hit && _shovel != null)
            {
                _shovel.SetActive(true);
                _shovel.transform.position = rh.point;
                // 朝向：让铲子大致面向相机，简单可用；以后可换成手势/朝向逻辑
                Vector3 toCam = _cam.transform.position - rh.point;
                toCam.y = 0;
                if (toCam.sqrMagnitude > 1e-4f)
                    _shovel.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            }
            else if (_shovel != null)
            {
                _shovel.SetActive(false);
            }

            if (hit && Input.GetMouseButtonDown(0))
            {
                Vector3 center = (_digPoint != null) ? _digPoint.position : rh.point;
                float rotY = _shovel != null ? _shovel.transform.eulerAngles.y : 0f;
                if (!IsLoaded)
                    Dig(center, rotY);
                else
                    Drop(center, rotY);
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
