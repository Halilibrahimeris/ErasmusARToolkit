// Steadies real-world depth on scanned surfaces (see RoomSurfaceView). ARCore Depth is noisy on floors and
// table tops, most of all on shiny tiles: in patches the real floor is measured closer than it is, so it
// hides the floor grid and the bottom of objects standing on it. The room scan knows exactly where the
// floor is, so each scanned surface is drawn three times, before anything else is depth-tested:
//
// 1. Mark (ZTest LEqual, no depth write): the surface, pulled toward the camera, sets stencil bit 128
//    wherever the measured depth is not nearer than the pulled surface. Those pixels show the surface
//    itself (or depth noise on it), not a real object in front of it.
// 2. Unmark (ZTest LEqual, stencil Equal): the surface, pushed away from the camera by as much, clears
//    the bit wherever the measured depth lies behind even that: the camera sees through the surface
//    there (the model is wrong, or the surface ends), so the measured depth is the better one.
// 3. Write (ZTest Always, depth write, stencil Equal): on the pixels that are still marked, the
//    surface's own depth, a little farther (_PushBack), replaces the measured one, and the bit is cleared.
//
// How far the surface is pulled depends on the distance: the noise of depth grows with the square of it.
// A point on the view ray that lies pull(d) = clamp(_NoiseBase + _NoiseK * d * d, _MinPull, _MaxPullHeight)
// meters above the surface is where the pulled surface is drawn. Real objects clearly in front of the
// surface (chair legs, people) keep their measured depth and still hide things behind them. Pulling
// scales about the camera, which keeps every point on its view ray, so all passes cover the same pixels.
// Only depth and stencil are written; no pass writes a depth of its own per pixel.
Shader "Erasmus AR Toolkit/Surface Depth"
{
    Properties
    {
        _NoiseBase("Noise Base (m above the surface)", Float) = 0.02
        _NoiseK("Noise Growth (m per square meter of distance)", Float) = 0.01
        _MinPull("Min Pull Height (m)", Float) = 0.03
        _MaxPullHeight("Max Pull Height (m)", Float) = 0.10
        _MaxPull("Max Pull (fraction of distance)", Range(0, 0.9)) = 0.25
        _Direction("Direction (1 toward the camera, -1 away, 0 none)", Float) = 0
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
            "Queue" = "Geometry-48"
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
                float _NoiseBase;
                float _NoiseK;
                float _MinPull;
                float _MaxPullHeight;
                float _MaxPull;
                float _Direction;
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
                float3 fromCamera = positionWS - cameraWS;

                // A point on the view ray that is pullHeight above the surface lies this fraction nearer.
                float distanceSquared = dot(fromCamera, fromCamera);
                float pullHeight = clamp(_NoiseBase + _NoiseK * distanceSquared, _MinPull, _MaxPullHeight);
                float heightAboveSurface = max(abs(cameraWS.y - positionWS.y), 0.05);
                float pull = min(pullHeight / heightAboveSurface, _MaxPull);
                float3 testedWS = cameraWS + fromCamera * (1.0 - _Direction * pull + _PushBack);
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
