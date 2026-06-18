using UnityEngine;

/// <summary>
/// 高度场沙地形：
/// - 网格分辨率 resolution × resolution
/// - 每个顶点持有 (height, wetness)
/// - 提供 Carve / Pile / Wet API
/// - 每帧运行塌陷模拟（基于安息角）
/// 
/// 坐标系约定：地形局部 XZ 平面，原点在中心，宽度 = size。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class SandTerrain : MonoBehaviour
{
    [Header("地形")]
    public int resolution = 128;
    public float size = 20f;
    public float initialHeight = 0.0f;
    public float maxHeight = 4f;

    [Header("塌陷模拟（安息角）")]
    [Tooltip("干沙的最大稳定坡度（tan）。约等于 tan(34°) ≈ 0.67")]
    public float dryAngleTan = 0.7f;
    [Tooltip("湿沙的最大稳定坡度（tan）。湿沙能立得很陡，接近垂直")]
    public float wetAngleTan = 5f;
    [Range(1, 8)]
    public int slumpIterationsPerFrame = 2;
    [Range(0f, 0.5f)]
    public float slumpRate = 0.3f;

    [Header("湿度")]
    [Range(0f, 1f)]
    public float wetnessDecayPerSecond = 0.02f;

    private int N => resolution + 1;        // 顶点边长
    private float Cell => size / resolution; // 每格物理尺寸（米）

    private Mesh _mesh;
    private Vector3[] _verts;
    private Color[] _colors;       // 用 vertex color 传湿度（R 通道）
    private float[] _h;            // 高度
    private float[] _w;            // 湿度
    private bool _dirtyMesh;
    private MeshCollider _meshCollider;

    void Awake()
    {
        BuildMesh();
    }

    void BuildMesh()
    {
        _h = new float[N * N];
        _w = new float[N * N];
        for (int i = 0; i < _h.Length; i++) _h[i] = initialHeight;

        _mesh = new Mesh();
        _mesh.name = "SandTerrainMesh";
        _mesh.indexFormat = N * N > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        _verts = new Vector3[N * N];
        _colors = new Color[N * N];
        Vector2[] uvs = new Vector2[N * N];

        float half = size * 0.5f;
        int idx = 0;
        for (int z = 0; z < N; z++)
        {
            for (int x = 0; x < N; x++)
            {
                _verts[idx] = new Vector3(x * Cell - half, _h[idx], z * Cell - half);
                uvs[idx] = new Vector2((float)x / resolution, (float)z / resolution);
                _colors[idx] = new Color(0, 0, 0, 1);
                idx++;
            }
        }

        int[] tris = new int[resolution * resolution * 6];
        int t = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = z * N + x;
                tris[t++] = i;
                tris[t++] = i + N;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + N;
                tris[t++] = i + N + 1;
            }
        }

        _mesh.vertices = _verts;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.colors = _colors;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = _mesh;

        _meshCollider = GetComponent<MeshCollider>();
        _meshCollider.sharedMesh = _mesh;

        // 绑定材质
        var mr = GetComponent<MeshRenderer>();
        if (mr.sharedMaterial == null)
        {
            Shader sandShader = Shader.Find("Sandcastle/Sand");
            if (sandShader == null) sandShader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sandShader);
            mat.SetColor("_BaseColor", new Color(0.92f, 0.82f, 0.62f));
            mr.sharedMaterial = mat;
        }
    }

    void Update()
    {
        // 每帧塌陷迭代
        for (int it = 0; it < slumpIterationsPerFrame; it++)
            SlumpStep();

        // 湿度自然蒸发
        if (wetnessDecayPerSecond > 0f)
        {
            float decay = wetnessDecayPerSecond * Time.deltaTime;
            for (int i = 0; i < _w.Length; i++)
                if (_w[i] > 0f) _w[i] = Mathf.Max(0f, _w[i] - decay);
        }

        if (_dirtyMesh)
        {
            UploadMesh();
            _dirtyMesh = false;
        }
    }

    /// <summary>世界坐标转网格索引（不裁切，可越界）。</summary>
    public bool WorldToCell(Vector3 worldPos, out int cx, out int cz)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        float half = size * 0.5f;
        float fx = (local.x + half) / Cell;
        float fz = (local.z + half) / Cell;
        cx = Mathf.RoundToInt(fx);
        cz = Mathf.RoundToInt(fz);
        return cx >= 0 && cx < N && cz >= 0 && cz < N;
    }

    /// <summary>在世界坐标处开挖（高度减少）。半径单位米。</summary>
    public void Carve(Vector3 worldPos, float radiusMeters, float depth)
    {
        ApplyBrush(worldPos, radiusMeters, -depth, 0f);
    }

    /// <summary>在世界坐标处堆沙（高度增加）。半径单位米。</summary>
    public void Pile(Vector3 worldPos, float radiusMeters, float amount)
    {
        ApplyBrush(worldPos, radiusMeters, amount, 0f);
    }

    /// <summary>在世界坐标处加湿。半径单位米。</summary>
    public void Wet(Vector3 worldPos, float radiusMeters, float wetAmount)
    {
        ApplyBrush(worldPos, radiusMeters, 0f, wetAmount);
    }

    /// <summary>核心笔刷：高斯落差地修改 h 和 w。</summary>
    void ApplyBrush(Vector3 worldPos, float radiusMeters, float heightDelta, float wetDelta)
    {
        if (!WorldToCell(worldPos, out int cx, out int cz)) return;

        int rCells = Mathf.CeilToInt(radiusMeters / Cell);
        int x0 = Mathf.Max(0, cx - rCells);
        int x1 = Mathf.Min(N - 1, cx + rCells);
        int z0 = Mathf.Max(0, cz - rCells);
        int z1 = Mathf.Min(N - 1, cz + rCells);

        float r2 = radiusMeters * radiusMeters;
        Vector3 localCenter = transform.InverseTransformPoint(worldPos);

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float wx = x * Cell - size * 0.5f;
                float wz = z * Cell - size * 0.5f;
                float dx = wx - localCenter.x;
                float dz = wz - localCenter.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;

                float falloff = 1f - Mathf.Sqrt(d2) / radiusMeters;
                falloff = Mathf.SmoothStep(0f, 1f, falloff);

                int i = z * N + x;
                if (heightDelta != 0f)
                {
                    _h[i] = Mathf.Clamp(_h[i] + heightDelta * falloff, -maxHeight, maxHeight);
                }
                if (wetDelta != 0f)
                {
                    _w[i] = Mathf.Clamp01(_w[i] + wetDelta * falloff);
                }
            }
        }
        _dirtyMesh = true;
    }

    /// <summary>
    /// 一次塌陷迭代：相邻格子高度差超过安息角阈值时转移沙量。
    /// 阈值 = lerp(dryTan, wetTan, wetness) * Cell
    /// </summary>
    void SlumpStep()
    {
        bool any = false;
        // 4 邻域，使用红黑棋盘式更新避免一帧内偏移
        for (int parity = 0; parity < 2; parity++)
        {
            for (int z = 0; z < N; z++)
            {
                for (int x = 0; x < N; x++)
                {
                    if (((x + z) & 1) != parity) continue;
                    int i = z * N + x;
                    float hi = _h[i];
                    float wi = _w[i];
                    float threshold = Mathf.Lerp(dryAngleTan, wetAngleTan, wi) * Cell;

                    // 4 邻
                    TryTransfer(i, x, z, x + 1, z, hi, wi, threshold, ref any);
                    TryTransfer(i, x, z, x - 1, z, hi, wi, threshold, ref any);
                    TryTransfer(i, x, z, x, z + 1, hi, wi, threshold, ref any);
                    TryTransfer(i, x, z, x, z - 1, hi, wi, threshold, ref any);
                }
            }
        }
        if (any) _dirtyMesh = true;
    }

    void TryTransfer(int i, int x, int z, int nx, int nz, float hi, float wi, float threshold, ref bool any)
    {
        if (nx < 0 || nx >= N || nz < 0 || nz >= N) return;
        int j = nz * N + nx;
        float diff = hi - _h[j];
        if (diff <= threshold) return;
        // 也要考虑邻居自己的湿度（取较小的稳定阈值，更宽松）
        float wj = _w[j];
        float pairThreshold = Mathf.Lerp(dryAngleTan, wetAngleTan, Mathf.Min(wi, wj)) * Cell;
        if (diff <= pairThreshold) return;

        float excess = (diff - pairThreshold) * slumpRate * 0.5f;
        _h[i] -= excess;
        _h[j] += excess;
        any = true;
    }

    void UploadMesh()
    {
        for (int i = 0; i < _verts.Length; i++)
        {
            _verts[i].y = _h[i];
            _colors[i].r = _w[i];
        }
        _mesh.vertices = _verts;
        _mesh.colors = _colors;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        // MeshCollider 需要重新赋值才更新
        _meshCollider.sharedMesh = null;
        _meshCollider.sharedMesh = _mesh;
    }
}
