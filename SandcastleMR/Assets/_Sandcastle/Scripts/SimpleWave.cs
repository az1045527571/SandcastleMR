using UnityEngine;

/// <summary>
/// 简易的海浪视觉效果——一个蓝绿色半透明平面，
/// 用顶点动画模拟 Gerstner 波，UV 滚动做泡沫。
/// Step 1 只做视觉，不做物理交互。
/// </summary>
public class SimpleWave : MonoBehaviour
{
    [Header("尺寸")]
    [Tooltip("水面平面边长（米）。略大于沙箱让海水漫过边缘")]
    public float size = 0.44f;

    [Header("高度")]
    [Tooltip("静止水面世界 Y。由 WaveSimulator 每帧覆写")]
    public float restWorldY = 0.04f;

    [Header("波浪")]
    public float waveHeight = 0.004f;
    public float waveSpeed = 0.8f;
    public float waveFreq = 12f;

    [Header("颜色")]
    public Color shallowColor = new Color(0.2f, 0.75f, 0.8f, 0.5f);
    public Color deepColor = new Color(0.05f, 0.3f, 0.45f, 0.7f);

    private Mesh _mesh;
    private Vector3[] _baseVerts;

    void Start()
    {
        // 创建细分平面
        _mesh = GeneratePlane(64, size);
        _baseVerts = _mesh.vertices.Clone() as Vector3[];

        var mf = gameObject.AddComponent<MeshFilter>();
        mf.mesh = _mesh;

        var mr = gameObject.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Material mat = new Material(shader);
        mat.SetColor("_BaseColor", shallowColor);
        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 0);
        mat.SetFloat("_Smoothness", 0.9f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mr.material = mat;

        // 静止位置在世界原点上方 restWorldY，X/Z 对齐原点
        transform.position = new Vector3(0f, restWorldY, 0f);
    }

    void Update()
    {
        // 简单正弦波顶点动画
        Vector3[] verts = _mesh.vertices;
        float t = Time.time * waveSpeed;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 b = _baseVerts[i];
            float y = Mathf.Sin(b.x * waveFreq + t)
                    * Mathf.Cos(b.z * waveFreq * 0.7f + t * 0.6f)
                    * waveHeight;
            verts[i] = new Vector3(b.x, y, b.z);
        }
        _mesh.vertices = verts;
        _mesh.RecalculateNormals();
    }

    Mesh GeneratePlane(int res, float planeSize)
    {
        Mesh m = new Mesh();
        m.name = "WavePlane";
        int vertCount = (res + 1) * (res + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[res * res * 6];

        float step = planeSize / res;
        float half = planeSize * 0.5f;
        int vi = 0;
        for (int z = 0; z <= res; z++)
        {
            for (int x = 0; x <= res; x++)
            {
                vertices[vi] = new Vector3(x * step - half, 0f, z * step - half);
                uvs[vi] = new Vector2((float)x / res, (float)z / res);
                vi++;
            }
        }
        int ti = 0;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = z * (res + 1) + x;
                triangles[ti++] = i;
                triangles[ti++] = i + res + 1;
                triangles[ti++] = i + 1;
                triangles[ti++] = i + 1;
                triangles[ti++] = i + res + 1;
                triangles[ti++] = i + res + 2;
            }
        }
        m.vertices = vertices;
        m.uv = uvs;
        m.triangles = triangles;
        m.RecalculateNormals();
        return m;
    }
}
