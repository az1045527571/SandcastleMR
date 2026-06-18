using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 运行时在 Game 视图用 LineRenderer 画 SDF 体积的 12 条边线。
    /// 自动跟随 SdfVolume 的 transform 和 size。
    /// </summary>
    [RequireComponent(typeof(SdfVolume))]
    public class SdfVolumeBoundsVisualizer : MonoBehaviour
    {
        public Color color = new Color(0.2f, 0.8f, 1f, 0.9f);
        public float lineWidth = 0.02f;

        private SdfVolume _volume;
        private LineRenderer[] _lines;

        void Start()
        {
            _volume = GetComponent<SdfVolume>();
            BuildLines();
        }

        void LateUpdate()
        {
            if (_volume == null) return;
            UpdateLines();
        }

        void BuildLines()
        {
            _lines = new LineRenderer[12];
            for (int i = 0; i < 12; i++)
            {
                var go = new GameObject($"BoundsLine_{i}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth;
                lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                lr.material.SetColor("_BaseColor", color);
                lr.startColor = color;
                lr.endColor = color;
                _lines[i] = lr;
            }
        }

        void UpdateLines()
        {
            Vector3 c = transform.position;
            Vector3 h = _volume.size * 0.5f;

            // 8 个角点
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = c + new Vector3( h.x, -h.y, -h.z);
            Vector3 p010 = c + new Vector3(-h.x,  h.y, -h.z);
            Vector3 p110 = c + new Vector3( h.x,  h.y, -h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y,  h.z);
            Vector3 p101 = c + new Vector3( h.x, -h.y,  h.z);
            Vector3 p011 = c + new Vector3(-h.x,  h.y,  h.z);
            Vector3 p111 = c + new Vector3( h.x,  h.y,  h.z);

            // 12 条边
            SetLine(0, p000, p100);
            SetLine(1, p100, p101);
            SetLine(2, p101, p001);
            SetLine(3, p001, p000);
            SetLine(4, p010, p110);
            SetLine(5, p110, p111);
            SetLine(6, p111, p011);
            SetLine(7, p011, p010);
            SetLine(8, p000, p010);
            SetLine(9, p100, p110);
            SetLine(10, p101, p111);
            SetLine(11, p001, p011);
        }

        void SetLine(int i, Vector3 a, Vector3 b)
        {
            var lr = _lines[i];
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }
    }
}
