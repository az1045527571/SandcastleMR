#ifndef SAND_DECALS_INCLUDED
#define SAND_DECALS_INCLUDED

// ============================================================
// 沙面法线贴花通用通道（可复用）
// 任何"往沙面盖一张法线透贴"的功能都走这里：脚印、手印、车辙、贝壳压痕…
// 由 SandDecalSystem.cs 在运行时填充全局 uniform。
//
// 数据约定：
//   _Decals[i]      : xy=世界XZ位置, z=朝向(弧度), w=强度(0~1, 含淡出)
//   _DecalParams[i] : x=贴图层索引, y=半尺寸(米,正方形不拉伸), z=反照率压暗(0~1), w=法线强度
//   _DecalTexArray  : Texture2DArray, 每层一张 OpenGL 法线图(rg=切线xy, a=遮罩)
// ============================================================

#define MAX_DECALS 32

float4 _Decals[MAX_DECALS];
float4 _DecalParams[MAX_DECALS];
int _DecalCount;

TEXTURE2D_ARRAY(_DecalTexArray);
SAMPLER(sampler_DecalTexArray);

// 在朝上表面叠加所有法线贴花。
//   worldXZ : 当前片元世界 XZ
//   upMask  : 朝上程度(0~1), 侧面/陡坡不盖贴花
//   N       : inout 世界法线
//   albedo  : inout 反照率
void ApplySandDecals(float2 worldXZ, float upMask, inout float3 N, inout float3 albedo)
{
    if (upMask <= 0.001 || _DecalCount <= 0) return;

    float2 accXZ = float2(0, 0);
    float darken = 0.0;

    [loop] for (int i = 0; i < MAX_DECALS; i++)
    {
        if (i >= _DecalCount) break;
        float4 dc = _Decals[i];
        float4 pp = _DecalParams[i];
        float size = max(pp.y, 1e-4);

        // 世界 → 贴花本地（按朝向反旋）
        float2 d = worldXZ - dc.xy;
        float ca = cos(-dc.z), sa = sin(-dc.z);
        float2 lp = float2(d.x * ca - d.y * sa, d.x * sa + d.y * ca);

        // 正方形映射，不拉伸：本地 [-size,size] → UV [0,1]
        float2 uv = lp / (2.0 * size) + 0.5;
        if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) continue;

        float4 tex = SAMPLE_TEXTURE2D_ARRAY(_DecalTexArray, sampler_DecalTexArray, uv, pp.x);
        float a = tex.a * dc.w;                 // 遮罩 × 强度(淡出)
        darken = max(darken, a * pp.z);         // 反照率压暗

        // 切线法线(取负翻转凹凸方向以匹配本场景朝向) → 旋回世界 XZ
        float2 nTan = -(tex.rg * 2.0 - 1.0) * a * pp.w;
        float cf = cos(dc.z), sf = sin(dc.z);
        accXZ += float2(nTan.x * cf + nTan.y * sf, -nTan.x * sf + nTan.y * cf);
    }

    N = normalize(N + float3(accXZ.x, 0, accXZ.y) * upMask);
    albedo *= (1.0 - saturate(darken) * upMask);
}

#endif // SAND_DECALS_INCLUDED
