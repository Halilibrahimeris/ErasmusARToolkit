// Steadies real-world depth on scanned surfaces (see RoomSurfaceView). ARCore Depth is noisy on floors and
// table tops, most of all on shiny tiles: in patches the real floor is measured closer than it is, so it
// hides the floor grid and the bottom of objects standing on it. The room scan knows exactly where the
// floor is, so each scanned surface is drawn twice, before anything else is depth-tested:
//
// 1. Mark (ZTest LEqual, no depth write): the surface, pulled toward the camera by the distance that
//    corresponds to _PullHeight above it along each view ray, sets stencil bit 128 wherever the measured
//    depth is not more than _PullHeight above the surface. Those pixels show the surface itself (or depth
//    noise), not a real object in front of it.
// 2. Write (ZTest Always, depth write, stencil Equal): on the marked pixels the surface's own depth, a
//    little farther (_PushBack), replaces the measured one, and the bit is cleared again.
//
// Real objects clearly in front of the surface (chair legs, people) keep their measured depth and still
// hide things behind them. Pulling toward the camera scales about the camera, which keeps every point on
// its view ray, so both passes cover the same pixels. Only depth and stencil are written.
Shader "Erasmus AR Toolkit/Surface Depth"
{
    Properties
    {
        _PullHeight("Pull Height (m above the surface)", Float) = 0
        _MaxPull("Max Pull (fraction of distance)", Range(0, 0.9)) = 0.35
        _PushBack("Push Back (fraction of distance)", Range(0, 0.05)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Compare", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass("Stencil Pass", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-40"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SurfaceDepth"

            ColorMask 0
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PullHeight;
                float _MaxPull;
                float _PushBack;
            CBUFFER_END

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
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 cameraWS = GetCameraPositionWS();

                // A point on the view ray that is _PullHeight above the surface lies this fraction nearer.
                float heightAboveSurface = max(abs(cameraWS.y - positionWS.y), 0.05);
                float pull = min(_PullHeight / heightAboveSurface, _MaxPull);
                float3 testedWS = cameraWS + (positionWS - cameraWS) * (1.0 - pull + _PushBack);
                output.positionCS = TransformWorldToHClip(testedWS);
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
