// GPU procedural 版沙子 shader (阶段一)
// 顶点从 StructuredBuffer<Vert> 用 SV_VertexID 取, 配合 DrawProceduralIndirect。
// 片元逻辑与 Sandcastle/Sand 一致(三面投影噪声+湿度+水位+贴花)。
// Vert 内存布局必须和 SandMarchingCubes.compute 的 struct Vert 一致: float3 pos; float3 normal; float wet;
Shader "Sandcastle/SandGPU"
{
    Properties
    {
        _BaseColor ("Base Color (干沙)", Color) = (0.92, 0.82, 0.62, 1)
        _WetColor ("Wet Color (湿沙)", Color) = (0.55, 0.45, 0.32, 1)
        _NoiseScale ("Noise Scale", Float) = 6.0
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.25
        _SparkleStrength ("Sparkle Strength", Range(0,1)) = 0.3
        _SparkleScale ("Sparkle Scale", Float) = 80.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _WetColor;
                float _NoiseScale;
                float _NoiseStrength;
                float _SparkleStrength;
                float _SparkleScale;
            CBUFFER_END

            float _GlobalWaterY;
            float _GlobalWetTransition;

            #include "SandDecals.hlsl"

            // 顶点 buffer (与 compute 的 struct Vert 对齐: float4 pos + float4 nw)
            struct Vert
            {
                float4 pos;
                float4 nw;  // xyz=normal, w=wet
            };
            StructuredBuffer<Vert> _VertBuf;
            // SdfVolume 的本地→世界矩阵 (procedural draw 不自动设 unity_ObjectToWorld)
            float4x4 _SandL2W;

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                float  wetness     : TEXCOORD3;
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    v += valueNoise(p) * amp;
                    p *= 2.0;
                    amp *= 0.5;
                }
                return v;
            }

            Varyings vert(uint id : SV_VertexID)
            {
                Vert vtx = _VertBuf[id];
                Varyings OUT;
                // pos/nw 已是世界坐标(compute 端变换完毕), 直接用
                float3 posWS = vtx.pos.xyz;
                float3 nWS   = normalize(vtx.nw.xyz);
                OUT.positionWS  = posWS;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS    = nWS;
                OUT.fogCoord    = ComputeFogFactor(OUT.positionHCS.z);
                OUT.wetness     = vtx.nw.w;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 absN = abs(normalize(IN.normalWS));
                float3 weights = absN / (absN.x + absN.y + absN.z + 0.0001);
                float2 uvXZ = IN.positionWS.xz;
                float2 uvXY = IN.positionWS.xy;
                float2 uvYZ = IN.positionWS.yz;

                float nXZ = fbm(uvXZ * _NoiseScale * 0.1);
                float nXY = fbm(uvXY * _NoiseScale * 0.1);
                float nYZ = fbm(uvYZ * _NoiseScale * 0.1);
                float n = nXZ * weights.y + nXY * weights.z + nYZ * weights.x;

                float grainXZ = valueNoise(uvXZ * _NoiseScale);
                float grainXY = valueNoise(uvXY * _NoiseScale);
                float grainYZ = valueNoise(uvYZ * _NoiseScale);
                float grain = grainXZ * weights.y + grainXY * weights.z + grainYZ * weights.x;

                float blend = saturate(n * 0.7 + grain * 0.3);

                float3 dryColor = _BaseColor.rgb * (blend * _NoiseStrength + (1.0 - _NoiseStrength));
                float3 wetColor = _WetColor.rgb * (blend * _NoiseStrength * 0.5 + (1.0 - _NoiseStrength * 0.5));
                float waterWet = saturate((_GlobalWaterY - IN.positionWS.y) / max(_GlobalWetTransition, 0.001));
                float wetness = max(IN.wetness, waterWet);
                float3 albedo = lerp(dryColor, wetColor, wetness);

                float spXZ = step(0.985, hash21(floor(uvXZ * _SparkleScale)));
                float spXY = step(0.985, hash21(floor(uvXY * _SparkleScale)));
                float spYZ = step(0.985, hash21(floor(uvYZ * _SparkleScale)));
                float sparkle = spXZ * weights.y + spXY * weights.z + spYZ * weights.x;
                albedo += sparkle * _SparkleStrength * (1.0 - wetness);

                float3 N = normalize(IN.normalWS);
                float upMask = saturate(N.y * 1.5 + 0.2);
                ApplySandDecals(IN.positionWS.xz, upMask, N, albedo);

                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                float3 diffuse = albedo * mainLight.color.rgb * NdotL;
                float3 ambient = SampleSH(N) * albedo;

                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), 64.0) * wetness * 0.3;

                float3 color = diffuse + ambient + spec;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
