// The boxes around scanned obstacles and the panels of scanned walls (see RoomSurfaceView): a
// see-through fill and edges in the accent color, like the furniture volumes of a VR headset's room model.
//
// A box is drawn around a real thing, a few centimeters outside of it, and with AR occlusion the noisy
// depth of that thing is in the depth buffer. For the depth test only, the box is pulled toward the camera
// by _PullMeters (same pixels, nearer depth), so the thing inside does not hide its own box, while
// something real that stands clearly in front of the box still does. The pull is kept between _MinPull
// and _MaxPull as fractions of the distance. The materials render before opaque objects (queue below
// 2000) and write no depth, so a box never hides a virtual object and never cuts into one.
//
// _FadeHeight: panels of walls fade out toward the top, so a wall shows where the room ends without
// standing in the way of the view. 0 = no fading (boxes).
Shader "Erasmus AR Toolkit/Obstacle"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 0.8, 0, 0.18)
        _PullMeters("Pull (m toward the camera)", Range(0, 0.5)) = 0.10
        _MinPull("Min Pull (fraction of distance)", Range(0, 0.2)) = 0.02
        _MaxPull("Max Pull (fraction of distance)", Range(0, 0.5)) = 0.25
        _FadeHeight("Fade Height (m above the floor, 0 = none)", Float) = 0
        _FloorHeight("Floor Height (world y)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Geometry-8"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Obstacle"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _PullMeters;
                float _MinPull;
                float _MaxPull;
                float _FadeHeight;
                float _FloorHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float heightWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 cameraWS = GetCameraPositionWS();
                float3 fromCamera = positionWS - cameraWS;

                // Scaling about the camera keeps every point on its view ray.
                float distanceToPoint = max(length(fromCamera), 0.05);
                float pull = clamp(_PullMeters / distanceToPoint, _MinPull, max(_MinPull, _MaxPull));
                output.positionCS = TransformWorldToHClip(cameraWS + fromCamera * (1.0 - pull));
                output.heightWS = positionWS.y - _FloorHeight;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _BaseColor;
                if (_FadeHeight > 0.0)
                {
                    color.a *= saturate(1.0 - input.heightWS / _FadeHeight);
                }

                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
