using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Sandcastle
{
    /// <summary>Burst 兼容的 piece 数据(球/盒/样条; bakedmesh 不进 job, CPU 局部补算)。</summary>
    public struct PieceData
    {
        public int type;            // 0=Sphere 1=Box 2=Spline
        public float4x4 worldToLocal; // Box 用
        public float3 sphereCenter; // Sphere 世界心
        public float sphereRadius;
        public float3 boxHalf;      // Box 半边
        public int splineStart, splineCount;
        public float splineRadius, splineTopY, splineBottomY;
    }

    /// <summary>
    /// 并行计算基础 SDF(沙层 box + 球/盒/样条 piece 的 SmoothMin 融合)。
    /// 每个体素独立 → IJobParallelFor + Burst SIMD, 替代原 CPU 单线程三重循环(584ms→数十ms)。
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct EvaluateBaseJob : IJobParallelFor
    {
        // 维度
        public int nx, ny, nz;             // 各轴顶点数 (res+1)
        public int rx0, ry0, rz0;          // 重算范围起点(体素索引)
        public int rnx, rny, rnz;          // 重算范围尺寸
        public float dx, dy, dz;
        public float3 size;
        public float sandThickness, sandInset, smoothK;
        public float4x4 localToWorld;      // 体积本地(角落原点-中心)→世界

        [ReadOnly] public NativeArray<PieceData> pieces;
        [ReadOnly] public NativeArray<float2> splinePts;

        [NativeDisableParallelForRestriction]
        public NativeArray<float> sdfBase;  // 全量输出, 只写范围内索引

        static float SmoothMin(float a, float b, float k)
        {
            if (k <= 1e-6f) return math.min(a, b);
            float h = math.saturate(0.5f + 0.5f * (b - a) / k);
            return math.lerp(b, a, h) - k * h * (1f - h);
        }

        static float SdfBoxLocal(float3 p, float3 center, float3 half)
        {
            float3 q = math.abs(p - center) - half;
            return math.length(math.max(q, 0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0f);
        }

        static float DistToSeg(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(math.dot(ab, ab), 1e-8f));
            return math.distance(p, a + t * ab);
        }

        float SamplePiece(float3 wp, in PieceData pc)
        {
            if (pc.type == 0)   // Sphere
                return math.distance(wp, pc.sphereCenter) - pc.sphereRadius;
            if (pc.type == 1)   // Box
            {
                float3 lp = math.mul(pc.worldToLocal, new float4(wp, 1f)).xyz;
                float3 q = math.abs(lp) - pc.boxHalf;
                return math.length(math.max(q, 0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0f);
            }
            // Spline
            float2 pxz = new float2(wp.x, wp.z);
            float distXZ = 1e9f;
            if (pc.splineCount == 1)
                distXZ = math.distance(pxz, splinePts[pc.splineStart]);
            else
                for (int i = 0; i < pc.splineCount - 1; i++)
                    distXZ = math.min(distXZ, DistToSeg(pxz, splinePts[pc.splineStart + i], splinePts[pc.splineStart + i + 1]));
            float horiz = distXZ - pc.splineRadius;
            float cy = (pc.splineTopY + pc.splineBottomY) * 0.5f;
            float halfY = (pc.splineTopY - pc.splineBottomY) * 0.5f;
            float vert = math.abs(wp.y - cy) - halfY;
            float qx = math.max(horiz, 0f), qy = math.max(vert, 0f);
            return math.sqrt(qx * qx + qy * qy) + math.min(math.max(horiz, vert), 0f);
        }

        public void Execute(int idx)
        {
            // idx 遍历重算范围 [0, rnx*rny*rnz)
            int lx = idx % rnx;
            int ly = (idx / rnx) % rny;
            int lz = idx / (rnx * rny);
            int x = rx0 + lx, y = ry0 + ly, z = rz0 + lz;
            if (x >= nx || y >= ny || z >= nz) return;

            float3 localPos = new float3(x * dx, y * dy, z * dz);
            float3 worldPos = math.mul(localToWorld, new float4(localPos - size * 0.5f, 1f)).xyz;

            // 沙层 box
            float3 boxCenter = new float3(size.x * 0.5f, sandThickness * 0.5f, size.z * 0.5f);
            float3 boxHalf = new float3(size.x * 0.5f - sandInset, sandThickness * 0.5f, size.z * 0.5f - sandInset);
            float d = SdfBoxLocal(localPos, boxCenter, boxHalf);

            for (int i = 0; i < pieces.Length; i++)
            {
                float di = SamplePiece(worldPos, pieces[i]);
                d = SmoothMin(d, di, smoothK);
            }

            bool atBorder = (x == 0 || x == nx - 1 || y == 0 || y == ny - 1 || z == 0 || z == nz - 1);
            if (atBorder) d = math.max(d, 0.01f);

            sdfBase[x + y * nx + z * nx * ny] = d;
        }
    }
}
