using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 标记一个 SDF 形状。挂在放置的构件 GameObject 上。
    /// 支持：Sphere, Box, BakedMesh（从 MeshSdfAsset 采样）
    /// </summary>
    public class SdfPiece : MonoBehaviour
    {
        public enum ShapeType { Sphere, Capsule, Box, BakedMesh, Spline }

        [Header("形状")]
        public ShapeType shape = ShapeType.Sphere;
        public float radius = 0.5f;

        [Header("Spline 沙堤参数")]
        [Tooltip("沿折线筑堤。控制点世界坐标")]
        public System.Collections.Generic.List<Vector3> splinePoints = new System.Collections.Generic.List<Vector3>();
        [Tooltip("堤半宽（米）")]
        public float splineRadius = 0.3f;
        [Tooltip("堤顶世界 Y（固定高度）。低于此高度的部分被填为实心")]
        public float splineTopY = 0f;
        [Tooltip("堤底世界 Y。从堤底到堤顶为实心沙")]
        public float splineBottomY = -0.5f;

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
                case ShapeType.Spline:
                    return SdfSpline(worldPos);
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

        /// <summary>
        /// 沿折线的固定高度沙堤。
        /// 水平：到折线（XZ 投影）的距离 - splineRadius；
        /// 垂直：限制在 splineBottomY ~ splineTopY；两者交集。
        /// </summary>
        float SdfSpline(Vector3 p)
        {
            if (splinePoints == null || splinePoints.Count == 0)
                return float.PositiveInfinity;

            Vector2 pxz = new Vector2(p.x, p.z);
            float distXZ;
            if (splinePoints.Count == 1)
            {
                Vector2 a = new Vector2(splinePoints[0].x, splinePoints[0].z);
                distXZ = Vector2.Distance(pxz, a);
            }
            else
            {
                distXZ = float.PositiveInfinity;
                for (int i = 0; i < splinePoints.Count - 1; i++)
                {
                    Vector2 a = new Vector2(splinePoints[i].x, splinePoints[i].z);
                    Vector2 b = new Vector2(splinePoints[i + 1].x, splinePoints[i + 1].z);
                    distXZ = Mathf.Min(distXZ, DistToSegment(pxz, a, b));
                }
            }
            float horiz = distXZ - splineRadius;

            float cy = (splineTopY + splineBottomY) * 0.5f;
            float halfY = (splineTopY - splineBottomY) * 0.5f;
            float vert = Mathf.Abs(p.y - cy) - halfY;

            float qx = Mathf.Max(horiz, 0f);
            float qy = Mathf.Max(vert, 0f);
            float outside = Mathf.Sqrt(qx * qx + qy * qy);
            float inside = Mathf.Min(Mathf.Max(horiz, vert), 0f);
            return outside + inside;
        }

        /// <summary>2D 点到线段距离。</summary>
        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Vector2.Dot(p - a, ab) / Mathf.Max(Vector2.Dot(ab, ab), 1e-8f);
            t = Mathf.Clamp01(t);
            Vector2 proj = a + t * ab;
            return Vector2.Distance(p, proj);
        }
    }
}
