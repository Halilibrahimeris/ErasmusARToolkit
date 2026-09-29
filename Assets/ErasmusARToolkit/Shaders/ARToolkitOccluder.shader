// Invisible stand-ins for scanned tables and other raised surfaces (see RoomScanner): the table top
// plus walls down to the floor, like the table volumes of a VR headset's room model. They write depth
// only, before the plane grids (RenderOrder.RoomOccluder), so the floor grid and virtual objects behind
// or under a table are hidden by it. The AR surface marks pull themselves toward the camera for the
// depth test (_DepthBias), so a table's own grid still shows on top of its occluder.
Shader "Erasmus AR Toolkit/Occluder"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-12"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Occluder"

            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
