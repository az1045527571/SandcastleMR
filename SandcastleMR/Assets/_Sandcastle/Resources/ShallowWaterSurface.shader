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
            int _GridH;
            float _LocalTideY;
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

                if (d < _MinDepthMat)
                {
                    // 干格：寻找周围 4 邻域中湿格的最大总水位 (terrain + depth)
                    float maxWater = -9999.0;
                    int x = IN.vid % _GridW;
                    int z = IN.vid / _GridW;

                    // Left
                    if (x > 0)
                    {
                        float nd = _DepthBuf[IN.vid - 1];
                        if (nd >= _MinDepthMat) maxWater = max(maxWater, _TerrainBuf[IN.vid - 1] + nd);
                    }
                    // Right
                    if (x < _GridW - 1)
                    {
                        float nd = _DepthBuf[IN.vid + 1];
                        if (nd >= _MinDepthMat) maxWater = max(maxWater, _TerrainBuf[IN.vid + 1] + nd);
                    }
                    // Down
                    if (z > 0)
                    {
                        float nd = _DepthBuf[IN.vid - _GridW];
                        if (nd >= _MinDepthMat) maxWater = max(maxWater, _TerrainBuf[IN.vid - _GridW] + nd);
                    }
                    // Up
                    if (z < _GridH - 1)
                    {
                        float nd = _DepthBuf[IN.vid + _GridW];
                        if (nd >= _MinDepthMat) maxWater = max(maxWater, _TerrainBuf[IN.vid + _GridW] + nd);
                    }

                    // 如果邻居有水，以邻居水位为准保持平整，否则 fallback 到基础潮汐水位
                    if (maxWater > -9900.0)
                        pos.y = maxWater;
                    else
                        pos.y = _LocalTideY;
                }
                else
                {
                    // 湿格：水面 = 地形底 + 水深
                    pos.y = terr + d;
                }

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
