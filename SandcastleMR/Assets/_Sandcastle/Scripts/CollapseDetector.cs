using System.Collections.Generic;
using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 塌陷检测器：
    /// 在 SdfVolume 侵蚀重建后运行，检查是否有实心体素与底面断开。
    /// 断开的部分提取成独立 mesh + Rigidbody 做物理掉落。
    /// </summary>
    public class CollapseDetector : MonoBehaviour
    {
        [Tooltip("塌陷检测间隔（秒）。每次侵蚀重建后检测一次")]
        public float checkInterval = 2f;
        [Tooltip("悬空体至少多少体素才触发塌陷（过滤噪声）")]
        public int minFloatingVoxels = 20;
        [Tooltip("掉落物存活时间（秒），之后销毁")]
        public float debrisLifetime = 3f;

        private SdfVolume _volume;
        private float _timer;

        void Start()
        {
            _volume = FindObjectOfType<SdfVolume>();
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= checkInterval)
            {
                _timer = 0f;
                if (_volume != null) CheckCollapse();
            }
        }

        void CheckCollapse()
        {
            int nx = _volume.resolutionX + 1;
            int ny = _volume.resolutionY + 1;
            int nz = _volume.resolutionZ + 1;
            float[] sdf = _volume.GetSdfData();
            if (sdf == null) return;

            // 找 SandTerrain 获取地面高度
            var terrain = FindObjectOfType<SandTerrain>();

            // 统计当前实心体素数
            int solidCount = 0;
            for (int i = 0; i < sdf.Length; i++)
                if (sdf[i] < 0f) solidCount++;

            Debug.Log($"[Collapse] 检测中... 实心体素={solidCount}, 总体素={sdf.Length}");

            // 计算每个体素世界位置
            float dx = _volume.size.x / _volume.resolutionX;
            float dy = _volume.size.y / _volume.resolutionY;
            float dz = _volume.size.z / _volume.resolutionZ;

            // Flood fill 仅从沙面以下开始（作为地基）并向上扩散
            // 这样只有与“地面”连通的实心才算有支撑
            bool[] supported = new bool[sdf.Length];
            Queue<int> queue = new Queue<int>();

            // 种子：那些 worldY < terrainHeight 的实心体素（即地面以下）
            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        int idx = x + y * nx + z * nx * ny;
                        if (sdf[idx] >= 0f) continue;

                        Vector3 localPos = new Vector3(x * dx, y * dy, z * dz);
                        Vector3 worldPos = _volume.LocalToWorld(localPos);

                        // 在沙面以下（地下）= 地基，是种子
                        float groundY = terrain != null ? terrain.SampleHeight(worldPos) : 0f;
                        if (worldPos.y < groundY - 0.05f)
                        {
                            supported[idx] = true;
                            queue.Enqueue(idx);
                        }
                    }
                }
            }

            // BFS flood fill（6 邻居）
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % nx;
                int y = (idx / nx) % ny;
                int z = idx / (nx * ny);

                TrySpread(x - 1, y, z, nx, ny, nz, sdf, supported, queue);
                TrySpread(x + 1, y, z, nx, ny, nz, sdf, supported, queue);
                TrySpread(x, y - 1, z, nx, ny, nz, sdf, supported, queue);
                TrySpread(x, y + 1, z, nx, ny, nz, sdf, supported, queue);
                TrySpread(x, y, z - 1, nx, ny, nz, sdf, supported, queue);
                TrySpread(x, y, z + 1, nx, ny, nz, sdf, supported, queue);
            }

            // 找出所有 不被支撑 且 实心(sdf<0) 的体素
            List<int> floating = new List<int>();
            for (int i = 0; i < sdf.Length; i++)
            {
                if (sdf[i] < 0f && !supported[i])
                    floating.Add(i);
            }

            if (floating.Count < minFloatingVoxels) return;

            Debug.Log($"[Collapse] 检测到 {floating.Count} 个悬空体素，触发塌陷！");

            // 从主 SDF 中删除悬空体素（设侵蚀值让它们变成空气）
            _volume.EraseVoxels(floating);
            _volume.RebuildMesh();

            // 计算悬空区域的质心（世界坐标）
            Vector3 center = ComputeCenter(floating, nx, ny, nz);

            // 生成掉落碎块
            SpawnDebris(center, floating.Count);
        }

        void TrySpread(int x, int y, int z, int nx, int ny, int nz,
                       float[] sdf, bool[] supported, Queue<int> queue)
        {
            if (x < 0 || x >= nx || y < 0 || y >= ny || z < 0 || z >= nz) return;
            int idx = x + y * nx + z * nx * ny;
            if (supported[idx]) return;
            if (sdf[idx] >= 0f) return; // 空气不传播
            supported[idx] = true;
            queue.Enqueue(idx);
        }

        Vector3 ComputeCenter(List<int> indices, int nx, int ny, int nz)
        {
            Vector3 sum = Vector3.zero;
            float dx = _volume.size.x / _volume.resolutionX;
            float dy = _volume.size.y / _volume.resolutionY;
            float dz = _volume.size.z / _volume.resolutionZ;

            foreach (int idx in indices)
            {
                int x = idx % nx;
                int y = (idx / nx) % ny;
                int z = idx / (nx * ny);
                Vector3 localPos = new Vector3(x * dx, y * dy, z * dz);
                sum += _volume.LocalToWorld(localPos);
            }
            return sum / indices.Count;
        }

        void SpawnDebris(Vector3 center, int voxelCount)
        {
            // 简化版：生成一个沙色球体代表碎块，大小按体素量估算
            float volume = voxelCount * Mathf.Pow(_volume.size.x / _volume.resolutionX, 3);
            float radius = Mathf.Pow(volume * 0.75f / Mathf.PI, 1f / 3f);
            radius = Mathf.Max(radius, 0.02f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Debris";
            go.transform.position = center;
            go.transform.localScale = Vector3.one * radius * 2f;

            // 沙色材质
            var r = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Sandcastle/Sand"));
            if (mat == null) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            r.sharedMaterial = mat;

            // 物理
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = volume * 1600f; // 沙密度 ~1600 kg/m³
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // 定时销毁
            Destroy(go, debrisLifetime);
        }
    }
}
