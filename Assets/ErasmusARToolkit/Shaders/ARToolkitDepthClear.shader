// Portal (project module): the doorway of a portal forgets the real world's depth, so the world inside
// is seen through the door even when a real wall stands just behind the portal. Writes the far depth and
// no colour, before the portal's world is drawn (RenderOrder.PortalMask).
Shader "Erasmus AR Toolkit/Depth Clear"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-11"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "DepthClear"

            ColorMask 0
            ZWrite On
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            struct Output
            {
                half4 color : SV_Target;
                float depth : SV_Depth;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            Output Frag(Varyings input)
            {
                Output output;
                output.color = 0;
                output.depth = UNITY_RAW_FAR_CLIP_VALUE;
                return output;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
