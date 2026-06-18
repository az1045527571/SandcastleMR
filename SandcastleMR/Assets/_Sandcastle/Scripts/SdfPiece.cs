using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 标记一个 SDF 形状。挂在放置的构件 GameObject 上。
    /// 目前只支持球形，后续扩展圆柱/胶囊/Box。
    /// 
    /// 放置时自动注册到场景里的 SdfVolume；
    /// 销毁时自动注销。
    /// </summary>
    public class SdfPiece : MonoBehaviour
    {
        public enum ShapeType { Sphere, Capsule, Box }

        [Header("形状")]
        public ShapeType shape = ShapeType.Sphere;
        public float radius = 0.3f;  // 单位：米（4cm 默认）

        private SdfVolume _volume;

        void OnEnable()
        {
            _volume = FindObjectOfType<SdfVolume>();
            if (_volume != null)
            {
                _volume.Register(this);
                Debug.Log($"[SdfPiece] Registered at {transform.position}, radius={radius}");
            }
            else
            {
                Debug.LogWarning("[SdfPiece] No SdfVolume found!");
            }
        }

        void OnDisable()
        {
            if (_volume != null) _volume.Unregister(this);
        }

        /// <summary>
        /// 返回世界坐标 p 处到此形状表面的带符号距离。
        /// 负 = 在表面内部，正 = 在表面外部。
        /// </summary>
        public float SampleSdf(Vector3 worldPos)
        {
            switch (shape)
            {
                case ShapeType.Sphere:
                    return SdfSphere(worldPos);
                default:
                    return SdfSphere(worldPos);
            }
        }

        float SdfSphere(Vector3 p)
        {
            return Vector3.Distance(p, transform.position) - radius * transform.lossyScale.x;
        }
    }
}
