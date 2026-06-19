using System.Collections.Generic;
using UnityEngine;

namespace Sandcastle
{
    /// <summary>
    /// 沙面法线贴花系统（可复用渲染线路）。
    /// 任何想"往沙面盖一张法线透贴"的玩法都用这套：脚印、手印、车辙、贝壳压痕…
    ///
    /// 用法：
    ///   int layer = SandDecalSystem.Instance.RegisterTexture(myNormalTex); // 注册一次，拿到层索引
    ///   SandDecalSystem.Instance.AddDecal(worldXZ, yaw, layer, size, darken, normalStrength, lifetime);
    ///
    /// 内部用 Texture2DArray 存所有贴花贴图（一个采样器），shader 端见 SandDecals.hlsl。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SandDecalSystem : MonoBehaviour
    {
        public const int MaxDecals = 32;
        const int TexSize = 512; // 所有贴花统一尺寸

        public static SandDecalSystem Instance { get; private set; }

        struct Decal
        {
            public Vector2 pos;       // 世界 XZ
            public float yaw;         // 朝向（弧度）
            public int layer;         // 贴图层
            public float size;        // 半尺寸（米）
            public float darken;      // 反照率压暗 0~1
            public float normalStr;   // 法线强度
            public float age;         // 已存活秒
            public float lifetime;    // 寿命秒（<=0 永久）
        }

        readonly List<Texture2D> _textures = new List<Texture2D>();
        readonly List<Decal> _decals = new List<Decal>();
        readonly Vector4[] _bufDecals = new Vector4[MaxDecals];
        readonly Vector4[] _bufParams = new Vector4[MaxDecals];
        Texture2DArray _texArray;
        bool _dirtyArray;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>注册一张法线贴图，返回它在数组里的层索引（复用同一张则返回已有索引）。</summary>
        public int RegisterTexture(Texture2D tex)
        {
            int existing = _textures.IndexOf(tex);
            if (existing >= 0) return existing;
            _textures.Add(tex);
            _dirtyArray = true;
            return _textures.Count - 1;
        }

        /// <summary>往沙面盖一张法线贴花。</summary>
        public void AddDecal(Vector2 worldXZ, float yaw, int layer,
                             float size, float darken, float normalStrength, float lifetime)
        {
            if (_decals.Count >= MaxDecals) _decals.RemoveAt(0); // 顶掉最旧
            _decals.Add(new Decal
            {
                pos = worldXZ, yaw = yaw, layer = layer,
                size = size, darken = darken, normalStr = normalStrength,
                age = 0f, lifetime = lifetime
            });
        }

        void RebuildArray()
        {
            if (_textures.Count == 0) return;
            _texArray = new Texture2DArray(TexSize, TexSize, _textures.Count,
                                           TextureFormat.RGBA32, false, true); // linear
            _texArray.wrapMode = TextureWrapMode.Clamp;
            _texArray.filterMode = FilterMode.Bilinear;
            for (int i = 0; i < _textures.Count; i++)
                Graphics.CopyTexture(_textures[i], 0, 0, _texArray, i, 0);
            Shader.SetGlobalTexture("_DecalTexArray", _texArray);
            _dirtyArray = false;
        }

        void Update()
        {
            if (_dirtyArray) RebuildArray();

            // 老化 + 淡出
            for (int i = _decals.Count - 1; i >= 0; i--)
            {
                var d = _decals[i];
                d.age += Time.deltaTime;
                if (d.lifetime > 0f && d.age >= d.lifetime) { _decals.RemoveAt(i); continue; }
                _decals[i] = d;
            }

            UploadToShader();
        }

        void UploadToShader()
        {
            int n = _decals.Count;
            for (int i = 0; i < n; i++)
            {
                var d = _decals[i];
                float fade = (d.lifetime > 0f) ? (1f - Mathf.Clamp01(d.age / d.lifetime)) : 1f;
                _bufDecals[i] = new Vector4(d.pos.x, d.pos.y, d.yaw, fade);
                _bufParams[i] = new Vector4(d.layer, d.size, d.darken, d.normalStr);
            }
            Shader.SetGlobalVectorArray("_Decals", _bufDecals);
            Shader.SetGlobalVectorArray("_DecalParams", _bufParams);
            Shader.SetGlobalInt("_DecalCount", n);
        }
    }
}
