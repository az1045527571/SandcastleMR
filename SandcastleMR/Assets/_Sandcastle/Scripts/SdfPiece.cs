using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 标记一个 SDF 形状。挂在放置的构件 GameObject 上。
    /// 支持：Sphere, Box, BakedMesh（从 MeshSdfAsset 采样）
    /// </summary>
    public class SdfPiece : MonoBehaviour
    {
        public enum ShapeType { Sphere, Capsule, Box, BakedMesh }

        [Header("形状")]
        public ShapeType shape = ShapeType.Sphere;
        public float radius = 0.5f;

        [Header("Baked Mesh SDF")]
        public MeshSdfAsset bakedSdf;

        private SdfVolume _volume;

        void OnEnable()
        {
            // 不在这里自动注册，由放置器设完参数后手动调 RegisterToVolume()
        }

        /// <summary>手动注册到 SdfVolume，应在设完参数后调用</summary>
        public void RegisterToVolume()
        {
            _volume = FindObjectOfType<SdfVolume>();
            if (_volume != null)
            {
                _volume.Register(this);
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
        /// </summary>
        public float SampleSdf(Vector3 worldPos)
        {
            switch (shape)
            {
                case ShapeType.Sphere:
                    return SdfSphere(worldPos);
                case ShapeType.Box:
                    return SdfBox(worldPos);
                case ShapeType.BakedMesh:
                    return SdfBaked(worldPos);
                default:
                    return SdfSphere(worldPos);
            }
        }

        float SdfSphere(Vector3 p)
        {
            return Vector3.Distance(p, transform.position) - radius * transform.lossyScale.x;
        }

        float SdfBox(Vector3 p)
        {
            Vector3 local = transform.InverseTransformPoint(p);
            Vector3 halfSize = Vector3.one * radius;
            Vector3 q = new Vector3(
                Mathf.Abs(local.x) - halfSize.x,
                Mathf.Abs(local.y) - halfSize.y,
                Mathf.Abs(local.z) - halfSize.z);
            float outside = new Vector3(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0), Mathf.Max(q.z, 0)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            return outside + inside;
        }

        float SdfBaked(Vector3 p)
        {
            if (bakedSdf == null) return float.PositiveInfinity;
            // 世界坐标 → mesh 本地坐标
            Vector3 local = transform.InverseTransformPoint(p);
            float d = bakedSdf.SampleAtLocal(local);
            // SDF 值要乘回世界缩放（本地距离 → 世界距离）
            d *= transform.lossyScale.x;
            return d;
        }
    }
}
