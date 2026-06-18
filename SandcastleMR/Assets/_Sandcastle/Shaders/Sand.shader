Shader "Sandcastle/Sand"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.92, 0.82, 0.62, 1)
        _DarkColor ("Dark Color (湿沙色)", Color) = (0.55, 0.45, 0.32, 1)
        _NoiseScale ("Noise Scale", Float) = 6.0
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.25
        _SparkleStrength ("Sparkle Strength", Range(0,1)) = 0.3
        _SparkleScale ("Sparkle Scale", Float) = 80.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.1
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

            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _DarkColor;
                float _NoiseScale;
                float _NoiseStrength;
                float _SparkleStrength;
                float _SparkleScale;
                float _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
            };

            // 简易 hash + value noise
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
                for (int i = 0; i < 4; i++)
                {
                    v += valueNoise(p) * amp;
                    p *= 2.0;
                    amp *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.normalWS    = vni.normalWS;
                OUT.uv          = IN.uv;
                OUT.fogCoord    = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 用世界坐标 XZ 平面采样，避免接缝
                float2 wpos = IN.positionWS.xz;

                // 大尺度起伏 + 小颗粒
                float n = fbm(wpos * _NoiseScale * 0.1);
                float grain = valueNoise(wpos * _NoiseScale);
                float blend = saturate(n * 0.7 + grain * 0.3);

                float3 albedo = lerp(_DarkColor.rgb, _BaseColor.rgb, blend * _NoiseStrength + (1.0 - _NoiseStrength));

                // 闪光颗粒（白色高光小点）
                float sparkle = step(0.985, hash21(floor(wpos * _SparkleScale)));
                albedo += sparkle * _SparkleStrength;

                // 简单 Lambert + 主光
                float3 N = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                float3 diffuse = albedo * mainLight.color.rgb * NdotL;

                // 环境光近似
                float3 ambient = SampleSH(N) * albedo;

                float3 color = diffuse + ambient;

                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }

    }
    FallBack "Universal Render Pipeline/Lit"
}
