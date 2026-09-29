// Everything the toolkit draws on a real surface: detected AR planes (fill and outline), the
// placement ring, the selection ring and the image target frame.
//
// Overlapping planes of one real surface look like a single surface because:
// - The texture is mapped in world space (x/z in meters times the tiling), so every plane shares
//   one grid whatever its rotation. This suits horizontal planes only (the toolkit detects only
//   those); a vertical plane would show stretched streaks.
// - ARPlaneSurfaces gives every surface its own stencil bit. The fill uses NotEqual + Replace, so a
//   pixel is painted once per surface; the outline uses NotEqual + Keep and draws after all fills,
//   so only the outer edge of the whole surface shows. With _StencilComp = Always (the default)
//   the stencil is not used at all.
//
// Real-world occlusion: with ARCore Depth the camera background writes the depth of the real world,
// and these marks are depth-tested against it, so a real table hides the floor grid behind it.
// That depth is noisy, so the surface a mark lies on would hide parts of it too. The mark is pulled
// toward the camera for the depth test only (same pixels, nearer depth): far enough that a real point
// less than _HeightTolerance above the surface cannot hide it. Seen at a flat angle, that height is a
// long way along the view ray, so flat views get a larger pull (_DepthBias at least, _MaxDepthBias at
// most, both fractions of the distance). The materials render before opaque objects (queue below 2000)
// and never write depth, so the pull cannot put a mark in front of a virtual object. On scanned
// surfaces the room scan also replaces the noisy depth itself (ARToolkitSurfaceDepth.shader).
Shader "Erasmus AR Toolkit/AR Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture (tiling = repeats per meter)", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _DepthBias("Depth Bias (min fraction of distance)", Range(0, 0.2)) = 0.05
        _HeightTolerance("Height Tolerance (m above the surface)", Range(0, 0.5)) = 0.12
        _MaxDepthBias("Max Depth Bias (fraction of distance)", Range(0, 0.5)) = 0.3
        [IntRange] _StencilBit("Stencil Bit", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Compare", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass("Stencil Pass", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Geometry-10"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ARSurface"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            Stencil
            {
                Ref [_StencilBit]
                ReadMask [_StencilBit]
                WriteMask [_StencilBit]
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _DepthBias;
                float _HeightTolerance;
                float _MaxDepthBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Scaling about the camera keeps every point on its view ray and horizontal triangles flat.
                float3 cameraWS = GetCameraPositionWS();
                float heightAboveSurface = max(abs(cameraWS.y - positionWS.y), 0.05);
                float bias = clamp(_HeightTolerance / heightAboveSurface, _DepthBias, max(_DepthBias, _MaxDepthBias));
                float3 depthTestedWS = cameraWS + (positionWS - cameraWS) * (1.0 - bias);
                output.positionCS = TransformWorldToHClip(depthTestedWS);

                output.uv = positionWS.xz * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
