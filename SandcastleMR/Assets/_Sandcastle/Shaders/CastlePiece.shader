Shader "Sandcastle/CastlePiece"
{
    Properties
    {
        _BaseColor ("Piece Color", Color) = (0.88, 0.78, 0.58, 1)
        _SandColor ("Sand Color (融合目标)", Color) = (0.92, 0.82, 0.62, 1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.15

        [Header(Blend Settings)]
        _BlendHeight ("Blend Height (融合带高度/米)", Float) = 0.4
        _BlendOffset ("Blend Offset (从沙面往上偏移)", Float) = 0.02
        _NoiseScale ("Noise Scale (融合边缘噪声)", Float) = 12.0
        _NoiseStrength ("Noise Strength", Range(0,0.3)) = 0.1
        _PieceBaseY ("Piece Base Y (脚下沙面世界高度)", Float) = 0.0
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
                float4 _SandColor;
                float _Smoothness;
                float _BlendHeight;
                float _BlendOffset;
                float _NoiseScale;
                float _NoiseStrength;
                float _PieceBaseY;
            CBUFFER_END

            // 从 SandTerrain 脚本传入的全局变量
            float _SandTerrainMinY;
            float _SandTerrainMaxY;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.normalWS    = vni.normalWS;
                OUT.fogCoord    = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 posWS = IN.positionWS;

                float sandSurface = _PieceBaseY + _BlendOffset;
                float heightAboveSand = posWS.y - sandSurface;

                // 噪声扰动边缘
                float noise = valueNoise(posWS.xz * _NoiseScale) * _NoiseStrength;
                heightAboveSand += noise;

                // 融合因子：0=沙面 1=构件
                float blend = saturate(heightAboveSand / max(_BlendHeight, 0.001));
                blend = smoothstep(0.0, 1.0, blend);

                // 颜色融合
                float3 albedo = lerp(_SandColor.rgb, _BaseColor.rgb, blend);

                // 【核心】法线融合：交界处法线从沙面朝上(0,1,0)渐变到构件自身法线
                float3 pieceN = normalize(IN.normalWS);
                float3 sandN = float3(0, 1, 0);
                float3 N = normalize(lerp(sandN, pieceN, blend));

                // 光照用融合后的法线
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                float3 diffuse = albedo * mainLight.color.rgb * NdotL;
                float3 ambient = SampleSH(N) * albedo;

                // 高光（融合区弱，顶部强）
                float3 V = normalize(GetWorldSpaceViewDir(posWS));
                float3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), 32.0) * _Smoothness * blend;

                float3 color = diffuse + ambient + spec;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
