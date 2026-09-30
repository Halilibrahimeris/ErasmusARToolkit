// Vision simulator (project module "See Through Other Eyes"): shows the camera view as someone with a
// colour vision deficiency, low vision, tunnel vision or a cataract sees it. A full-screen quad samples
// URP's opaque texture (the camera picture plus every opaque object) and draws it back filtered. It draws
// before ordinary transparent things (queue Transparent-100), so info cards stay sharp and readable.
// The vertex shader puts the quad over the whole screen, whatever the camera's projection (AR cameras
// use the phone camera's own projection).
Shader "Erasmus AR Toolkit/Vision Filter"
{
    Properties
    {
        _Row0 ("Color Row 0", Vector) = (1, 0, 0, 0)
        _Row1 ("Color Row 1", Vector) = (0, 1, 0, 0)
        _Row2 ("Color Row 2", Vector) = (0, 0, 1, 0)
        _Blur ("Blur (screen fraction)", Float) = 0
        _Tunnel ("Tunnel Radius (0 = off)", Float) = 0
        _Haze ("Haze", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VisionFilter"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Row0;
                float4 _Row1;
                float4 _Row2;
                float _Blur;
                float _Tunnel;
                float _Haze;
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
                // A Unity quad spans -0.5 .. 0.5: stretched over the whole screen.
                output.positionCS = float4(input.positionOS.xy * 2.0, 0.5, 1.0);
                return output;
            }

            half3 Blurred(float2 uv)
            {
                float2 step = float2(_Blur, _Blur * _ScreenParams.x / _ScreenParams.y);
                half3 sum = SampleSceneColor(uv) * 2.0;
                float weight = 2.0;
                [unroll]
                for (int ring = 1; ring <= 2; ring++)
                {
                    float2 offset = step * ring * 0.5;
                    sum += SampleSceneColor(uv + float2(offset.x, 0));
                    sum += SampleSceneColor(uv - float2(offset.x, 0));
                    sum += SampleSceneColor(uv + float2(0, offset.y));
                    sum += SampleSceneColor(uv - float2(0, offset.y));
                    sum += SampleSceneColor(uv + offset);
                    sum += SampleSceneColor(uv - offset);
                    sum += SampleSceneColor(uv + float2(offset.x, -offset.y));
                    sum += SampleSceneColor(uv + float2(-offset.x, offset.y));
                    weight += 8.0;
                }

                return sum / weight;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                half3 color = _Blur > 0.0 ? Blurred(uv) : SampleSceneColor(uv);
                color = half3(dot(_Row0.xyz, color), dot(_Row1.xyz, color), dot(_Row2.xyz, color));
                color = lerp(color, half3(0.86, 0.83, 0.74), _Haze);

                if (_Tunnel > 0.0)
                {
                    float2 fromCenter = (uv - 0.5) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                    color *= 1.0 - smoothstep(_Tunnel, _Tunnel + 0.12, length(fromCenter));
                }

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
