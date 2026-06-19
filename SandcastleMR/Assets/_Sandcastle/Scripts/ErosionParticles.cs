using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 侵蚀碎屑粒子特效：海浪冲刷沙堡表面时，在被冲掉的体素位置喷出沙色碎屑。
    /// 由 WaveSimulator 在侵蚀时调用 Emit(points)。
    /// 运行时自动创建一个 ParticleSystem，不依赖手动配置。
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class ErosionParticles : MonoBehaviour
    {
        [Tooltip("每个侵蚀点喷出的粒子数")]
        public int particlesPerPoint = 2;
        [Tooltip("碎屑初速度（米/秒）")]
        public float driftSpeed = 0.03f;
        public Color sandColor = new Color(0.85f, 0.74f, 0.55f, 1f);

        private ParticleSystem _ps;

        public static ErosionParticles Create(Transform parent)
        {
            var go = new GameObject("ErosionParticles");
            go.transform.SetParent(parent, false);
            return go.AddComponent<ErosionParticles>();
        }

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.8f;
            main.startSize = 0.003f;
            main.startSpeed = 0f;          // 速度由 Emit 时手动给
            main.startColor = sandColor;
            main.gravityModifier = 0.6f;   // 受重力下落
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _ps.emission;
            emission.enabled = false;       // 只用手动 Emit

            var shape = _ps.shape;
            shape.enabled = false;

            // 渲染器材质
            var psr = GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                var mat = new Material(sh);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", sandColor);
                psr.sharedMaterial = mat;
            }
            psr.renderMode = ParticleSystemRenderMode.Billboard;
        }

        /// <summary>在给定世界坐标点喷出碎屑。</summary>
        public void Emit(System.Collections.Generic.List<Vector3> points)
        {
            if (points == null || points.Count == 0) return;

            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < points.Count; i++)
            {
                for (int k = 0; k < particlesPerPoint; k++)
                {
                    ep.position = points[i];
                    // 朝半球随机方向飘散，略带向上
                    Vector3 dir = Random.insideUnitSphere;
                    dir.y = Mathf.Abs(dir.y) * 0.5f + 0.2f;
                    ep.velocity = dir.normalized * driftSpeed * Random.Range(0.5f, 1.2f);
                    ep.startLifetime = Random.Range(0.5f, 1.0f);
                    ep.startSize = Random.Range(0.002f, 0.005f);
                    ep.startColor = sandColor;
                    _ps.Emit(ep, 1);
                }
            }
        }
    }
}
