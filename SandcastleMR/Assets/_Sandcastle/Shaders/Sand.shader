Shader "Sandcastle/Sand"
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

            // 全局水位（世界 Y）、湿过渡带
            float _GlobalWaterY;
            float _GlobalWetTransition;

            // 沙面法线贴花通用通道（脚印/手印/车辙…都走这里）
            #include "SandDecals.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;  // R = wetness
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
                float  wetness     : TEXCOORD4;
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
                OUT.wetness     = IN.color.r;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 三面投影采样：根据法线权重混合 XZ/XY/YZ 三个平面
                float3 absN = abs(normalize(IN.normalWS));
                float3 weights = absN / (absN.x + absN.y + absN.z + 0.0001);
                float2 uvXZ = IN.positionWS.xz;
                float2 uvXY = IN.positionWS.xy;
                float2 uvYZ = IN.positionWS.yz;

                // 大尺度 fbm
                float nXZ = fbm(uvXZ * _NoiseScale * 0.1);
                float nXY = fbm(uvXY * _NoiseScale * 0.1);
                float nYZ = fbm(uvYZ * _NoiseScale * 0.1);
                float n = nXZ * weights.y + nXY * weights.z + nYZ * weights.x;

                // 小尺度颗粒
                float grainXZ = valueNoise(uvXZ * _NoiseScale);
                float grainXY = valueNoise(uvXY * _NoiseScale);
                float grainYZ = valueNoise(uvYZ * _NoiseScale);
                float grain = grainXZ * weights.y + grainXY * weights.z + grainYZ * weights.x;

                float blend = saturate(n * 0.7 + grain * 0.3);

                // 干沙颜色 + 噪声
                float3 dryColor = _BaseColor.rgb * (blend * _NoiseStrength + (1.0 - _NoiseStrength));
                // 湿沙颜色
                float3 wetColor = _WetColor.rgb * (blend * _NoiseStrength * 0.5 + (1.0 - _NoiseStrength * 0.5));
                // 按湿度混合：顶点 vertex color 传递的 wetness + 世界水位判断
                float waterWet = saturate((_GlobalWaterY - IN.positionWS.y) / max(_GlobalWetTransition, 0.001));
                float wetness = max(IN.wetness, waterWet);
                float3 albedo = lerp(dryColor, wetColor, wetness);

                // 闪光颗粒（三面混合）
                float spXZ = step(0.985, hash21(floor(uvXZ * _SparkleScale)));
                float spXY = step(0.985, hash21(floor(uvXY * _SparkleScale)));
                float spYZ = step(0.985, hash21(floor(uvYZ * _SparkleScale)));
                float sparkle = spXZ * weights.y + spXY * weights.z + spYZ * weights.x;
                albedo += sparkle * _SparkleStrength * (1.0 - wetness);

                // 世界法线
                float3 N = normalize(IN.normalWS);

                // ===== 沙面法线贴花（脚印等，仅朝上面）=====
                float upMask = saturate(N.y * 1.5 + 0.2); // 放宽门限避免误杀
                ApplySandDecals(IN.positionWS.xz, upMask, N, albedo);

                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                float3 diffuse = albedo * mainLight.color.rgb * NdotL;

                // 环境光
                float3 ambient = SampleSH(N) * albedo;

                // 湿沙反光
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
    FallBack "Universal Render Pipeline/Lit"
}
