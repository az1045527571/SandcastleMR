using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 烘焙好的 mesh SDF 资产。
    /// </summary>
    [CreateAssetMenu(menuName = "Sandcastle/Mesh SDF Asset", fileName = "MeshSdf")]
    public class MeshSdfAsset : ScriptableObject
    {
        public Bounds bounds;
        public Vector3Int resolution = new Vector3Int(32, 32, 32);
        public Texture3D sdfTex;

        [System.NonSerialized]
        private float[] _cachedSdf;

        void EnsureCache()
        {
            if (_cachedSdf != null || sdfTex == null) return;
            var pixels = sdfTex.GetPixels();
            _cachedSdf = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) _cachedSdf[i] = pixels[i].r;
        }

        /// <summary>本地坐标 → SDF 值</summary>
        public float SampleAtLocal(Vector3 localPos)
        {
            if (sdfTex == null) return float.PositiveInfinity;
            EnsureCache();

            Vector3 min = bounds.min;
            Vector3 size = bounds.size;
            Vector3 uvw;
            uvw.x = (localPos.x - min.x) / size.x;
            uvw.y = (localPos.y - min.y) / size.y;
            uvw.z = (localPos.z - min.z) / size.z;

            // 包围盒外：估算到包围盒的距离
            if (uvw.x < 0 || uvw.x > 1 || uvw.y < 0 || uvw.y > 1 || uvw.z < 0 || uvw.z > 1)
            {
                Vector3 clamped = new Vector3(
                    Mathf.Clamp01(uvw.x),
                    Mathf.Clamp01(uvw.y),
                    Mathf.Clamp01(uvw.z));
                Vector3 outside = uvw - clamped;
                outside.x *= size.x;
                outside.y *= size.y;
                outside.z *= size.z;
                float boundary = SampleTex3D(clamped);
                return boundary + outside.magnitude;
            }

            return SampleTex3D(uvw);
        }

        float SampleTex3D(Vector3 uvw)
        {
            int rx = resolution.x;
            int ry = resolution.y;
            int rz = resolution.z;
            float fx = uvw.x * (rx - 1);
            float fy = uvw.y * (ry - 1);
            float fz = uvw.z * (rz - 1);
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            int z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, rx - 1);
            int y1 = Mathf.Min(y0 + 1, ry - 1);
            int z1 = Mathf.Min(z0 + 1, rz - 1);
            float tx = fx - x0;
            float ty = fy - y0;
            float tz = fz - z0;

            float c000 = _cachedSdf[Idx(x0, y0, z0, rx, ry)];
            float c100 = _cachedSdf[Idx(x1, y0, z0, rx, ry)];
            float c010 = _cachedSdf[Idx(x0, y1, z0, rx, ry)];
            float c110 = _cachedSdf[Idx(x1, y1, z0, rx, ry)];
            float c001 = _cachedSdf[Idx(x0, y0, z1, rx, ry)];
            float c101 = _cachedSdf[Idx(x1, y0, z1, rx, ry)];
            float c011 = _cachedSdf[Idx(x0, y1, z1, rx, ry)];
            float c111 = _cachedSdf[Idx(x1, y1, z1, rx, ry)];

            float c00 = Mathf.Lerp(c000, c100, tx);
            float c10 = Mathf.Lerp(c010, c110, tx);
            float c01 = Mathf.Lerp(c001, c101, tx);
            float c11 = Mathf.Lerp(c011, c111, tx);
            float c0 = Mathf.Lerp(c00, c10, ty);
            float c1 = Mathf.Lerp(c01, c11, ty);
            return Mathf.Lerp(c0, c1, tz);
        }

        int Idx(int x, int y, int z, int rx, int ry) => x + y * rx + z * rx * ry;
    }
}
