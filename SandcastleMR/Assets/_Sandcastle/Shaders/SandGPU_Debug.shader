// GPU 沙子最小化诊断 shader - 纯红输出, 无光照/噪声/include
// 用途: 隔离 "DrawProcedural 是否在画 + StructuredBuffer 是否绑上"
// 若出现红色三角 = draw和buffer都OK, 问题在主shader的光照/include
Shader "Sandcastle/SandGPU_Debug"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Vert { float4 pos; float4 nw; };
            StructuredBuffer<Vert> _VertBuf;

            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert(uint id : SV_VertexID)
            {
                Varyings OUT;
                float3 posWS = _VertBuf[id].pos.xyz;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(1, 0, 0, 1); // 纯红
            }
            ENDHLSL
        }
    }
}
