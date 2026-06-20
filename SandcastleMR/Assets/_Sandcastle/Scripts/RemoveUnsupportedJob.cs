using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Sandcastle
{
    /// <summary>
    /// Burst 连通域检测: 以最底两层实心体素为地基种子, 6邻 BFS 向上扩散标记 supported。
    /// 输出 supported 位图 + 无支撑实心体素索引列表(removedIdx)。
    /// 擦除(_erosion/_carved)和世界坐标在主线程做(需 SdfVolume 字段)。
    /// 替代原 CPU 单线程 Queue BFS(242万体素 ~20ms → 数ms)。
    /// </summary>
    [BurstCompile]
    public struct RemoveUnsupportedJob : IJob
    {
        public int nx, ny, nz;
        [ReadOnly] public NativeArray<float> sdf;     // base+erosion, <0=实心

        public NativeArray<byte> supported;           // 输出: 1=与地基连通
        public NativeArray<int> queue;                // BFS 队列缓冲(容量=体素数)
        public NativeList<int> removedIdx;            // 输出: 无支撑实心体素索引

        int Index(int x, int y, int z) => x + y * nx + z * nx * ny;

        public void Execute()
        {
            int total = nx * ny * nz;
            for (int i = 0; i < total; i++) supported[i] = 0;

            int head = 0, tail = 0;

            // 地基种子: 最底两层(y<=1)实心体素
            for (int z = 0; z < nz; z++)
                for (int y = 0; y <= 1 && y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        int idx = Index(x, y, z);
                        if (sdf[idx] >= 0f) continue;
                        if (supported[idx] != 0) continue;
                        supported[idx] = 1;
                        queue[tail++] = idx;
                    }

            // 6邻 BFS
            while (head < tail)
            {
                int idx = queue[head++];
                int x = idx % nx;
                int y = (idx / nx) % ny;
                int z = idx / (nx * ny);
                Spread(x - 1, y, z, ref tail, queue);
                Spread(x + 1, y, z, ref tail, queue);
                Spread(x, y - 1, z, ref tail, queue);
                Spread(x, y + 1, z, ref tail, queue);
                Spread(x, y, z - 1, ref tail, queue);
                Spread(x, y, z + 1, ref tail, queue);
            }

            // 收集无支撑的实心体素
            for (int i = 0; i < total; i++)
                if (sdf[i] < 0f && supported[i] == 0)
                    removedIdx.Add(i);
        }

        void Spread(int x, int y, int z, ref int tail, NativeArray<int> q)
        {
            if (x < 0 || x >= nx || y < 0 || y >= ny || z < 0 || z >= nz) return;
            int idx = Index(x, y, z);
            if (supported[idx] != 0 || sdf[idx] >= 0f) return;
            supported[idx] = 1;
            q[tail++] = idx;
        }
    }
}
