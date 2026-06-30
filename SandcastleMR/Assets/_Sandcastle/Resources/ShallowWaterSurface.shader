Shader "Sandcastle/ShallowWaterSurface"
{
    Properties
    {
        _ShallowColor ("浅水色", Color) = (0.3, 0.6, 0.9, 0.45)
        _DeepColor    ("深水色", Color) = (0.05, 0.25, 0.5, 0.8)
        _DepthForFull ("深水满色所需水深(米)", Float) = 0.15
        _RefractionStrength ("折射强度", Float) = 0.01
        _CausticStrength ("焦散强度", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            StructuredBuffer<float> _DepthBuf;
            StructuredBuffer<float> _TerrainBuf;
            int _GridW;
            float _MinDepthMat;
            float4 _ShallowColor;
            float4 _DeepColor;
            float _DepthForFull;
            float _RefractionStrength;
            float _CausticStrength;

            struct Attributes { float3 positionOS : POSITION; uint vid : SV_VertexID; };
            struct Varyings { float4 positionHCS : SV_POSITION; float depth : TEXCOORD0; float3 localPos : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float terr = _TerrainBuf[IN.vid];
                float d = _DepthBuf[IN.vid];
                float3 pos = IN.positionOS;
                pos.y = terr + max(d, 0.0);
                o.positionHCS = TransformObjectToHClip(pos);
                o.depth = d;
                o.localPos = pos;
                return o;
            }

            float WaveCaustics(float2 uv, float time)
            {
                // 用 4 组不同方向和速度的简易正弦波叠加模拟焦散网络
                float2 p = uv * 40.0;
                float2 d1 = float2(cos(0.0), sin(0.0));
                float w1 = sin(dot(p, d1) + time * 1.8);
                
                float2 d2 = float2(cos(1.0), sin(1.0));
                float w2 = sin(dot(p, d2) - time * 2.2);
                
                float2 d3 = float2(cos(2.2), sin(2.2));
                float w3 = sin(dot(p, d3) + time * 1.4);
                
                float2 d4 = float2(cos(3.5), sin(3.5));
                float w4 = sin(dot(p, d4) - time * 1.9);
                
                float waveSum = (w1 + w2 + w3 + w4) * 0.25;
                return pow(1.0 - abs(waveSum), 8.0); // 锐化边缘，形成亮纹线网
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (IN.depth < _MinDepthMat)
                    discard;

                // 1. 获取屏幕空间 UV 坐标
                float2 screenUV = IN.positionHCS.xy / _ScreenParams.xy;

                // 2. 计算基于时间的动态折射扰动
                float2 distortion = float2(
                    sin(IN.localPos.x * 25.0 + _Time.y * 2.0) * cos(IN.localPos.z * 20.0 + _Time.y * 1.5),
                    cos(IN.localPos.x * 20.0 - _Time.y * 1.7) * sin(IN.localPos.z * 25.0 + _Time.y * 2.2)
                ) * _RefractionStrength;

                // 水深越浅，折射扰动越小，边缘过渡更平滑
                distortion *= saturate(IN.depth * 8.0);

                // 3. 采样背景抓屏纹理（折射效果）
                float3 sceneColor = SampleSceneColor(screenUV + distortion);

                // 4. 叠加模拟水底的假焦散
                float caustVal = WaveCaustics(IN.localPos.xz, _Time.y);
                float3 causticColor = float3(1.0, 0.95, 0.88); // 暖白日光投射色
                
                // 随着深度衰减，水面极浅处或无水处不显示焦散
                float caustFade = saturate(IN.depth * 15.0) * exp(-IN.depth * 3.0);
                sceneColor += causticColor * caustVal * caustFade * _CausticStrength;

                // 5. 混合水面本身的吸收和反射颜色
                float t = saturate(IN.depth / max(_DepthForFull, 1e-4));
                half4 waterCol = lerp(_ShallowColor, _DeepColor, t);

                // 水深越深，水面本身颜色占比越高
                float3 finalColor = lerp(sceneColor, waterCol.rgb, waterCol.a);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
