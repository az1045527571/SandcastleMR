using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 烘焙好的 mesh SDF 资产。
    /// - bounds：mesh 的本地包围盒（含 padding）
    /// - sdfTex：3D 纹理 RFloat，存到表面带符号距离（mesh 局部坐标系下）
    /// - resolution：3D 纹理分辨率（每个轴）
    /// 
    /// 运行时采样：
    /// 世界坐标 → 转 prefab 本地坐标 → 归一化到 [0,1] → 采样 3D 纹理
    /// </summary>
    [CreateAssetMenu(menuName = "Sandcastle/Mesh SDF Asset", fileName = "MeshSdf")]
    public class MeshSdfAsset : ScriptableObject
    {
        public Bounds bounds;
        public Vector3Int resolution = new Vector3Int(32, 32, 32);
        public Texture3D sdfTex;

        /// <summary>世界坐标 → 该资产的 SDF 值（已乘缩放）。</summary>
        public float SampleAtLocal(Vector3 localPos)
        {
            if (sdfTex == null) return float.PositiveInfinity;

            Vector3 min = bounds.min;
            Vector3 size = bounds.size;
            // 归一化到 [0,1]
            Vector3 uvw;
            uvw.x = (localPos.x - min.x) / size.x;
            uvw.y = (localPos.y - min.y) / size.y;
            uvw.z = (localPos.z - min.z) / size.z;

            // 超出包围盒：返回到包围盒最近距离的近似（取边界值 + 距离 box）
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
            // 三线性插值采样
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

            // 读 8 个角
            var pixels = sdfTex.GetPixels();
            float c000 = pixels[Idx(x0, y0, z0, rx, ry)].r;
            float c100 = pixels[Idx(x1, y0, z0, rx, ry)].r;
            float c010 = pixels[Idx(x0, y1, z0, rx, ry)].r;
            float c110 = pixels[Idx(x1, y1, z0, rx, ry)].r;
            float c001 = pixels[Idx(x0, y0, z1, rx, ry)].r;
            float c101 = pixels[Idx(x1, y0, z1, rx, ry)].r;
            float c011 = pixels[Idx(x0, y1, z1, rx, ry)].r;
            float c111 = pixels[Idx(x1, y1, z1, rx, ry)].r;

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
