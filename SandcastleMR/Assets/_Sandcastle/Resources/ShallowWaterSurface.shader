Shader "Sandcastle/ShallowWaterSurface"
{
    // 阶段一最简水面: 顶点读 compute 的水深+地形 buffer 抬高 Y, 干格塌到下方隐藏。
    // unlit 半透明蓝, 仅验证求解器(水流动/积水)。折射/焦散/菲涅尔后续美术阶段。
    Properties
    {
        _ShallowColor ("浅水色", Color) = (0.3, 0.6, 0.9, 0.45)
        _DeepColor    ("深水色", Color) = (0.05, 0.25, 0.5, 0.8)
        _DepthForFull ("深水满色所需水深(米)", Float) = 0.15
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

            StructuredBuffer<float> _DepthBuf;
            StructuredBuffer<float> _TerrainBuf;
            int _GridW;
            float _MinDepthMat;
            float4 _ShallowColor;
            float4 _DeepColor;
            float _DepthForFull;

            struct Attributes { float3 positionOS : POSITION; uint vid : SV_VertexID; };
            struct Varyings { float4 positionHCS : SV_POSITION; float depth : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float terr = _TerrainBuf[IN.vid];
                float d = _DepthBuf[IN.vid];
                float3 pos = IN.positionOS;
                if (d < _MinDepthMat)
                    pos.y = terr - 1.0;        // 干格: 塌到地形下方, 视觉隐藏
                else
                    pos.y = terr + d;          // 水面 = 地形底 + 水深
                o.positionHCS = TransformObjectToHClip(pos);
                o.depth = d;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = saturate(IN.depth / max(_DepthForFull, 1e-4));
                half4 col = lerp(_ShallowColor, _DeepColor, t);
                return col;
            }
            ENDHLSL
        }
    }
}
