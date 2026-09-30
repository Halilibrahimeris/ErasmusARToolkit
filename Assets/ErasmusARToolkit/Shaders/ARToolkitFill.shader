// Room fill (project module "Room Fill"): a see-through surface of water or a layer of smoke that rises in
// the room. It is depth-tested against the real world (AR occlusion), so a real table higher than the
// water stays dry and whatever is lower looks as if it were under water.
Shader "Erasmus AR Toolkit/Fill"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.2, 0.55, 0.85, 0.55)
        _WaveHeight ("Wave Height (m)", Float) = 0.01
        _WaveScale ("Wave Scale", Float) = 3
        _WaveSpeed ("Wave Speed", Float) = 1
        _Smoke ("Smoke (0 = water)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Fill"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _WaveHeight;
                float _WaveScale;
                float _WaveSpeed;
                float _Smoke;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float wave : TEXCOORD1;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), u.x),
                            lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _WaveSpeed;
                float wave = sin(world.x * _WaveScale + t) * cos(world.z * _WaveScale * 0.8 + t * 1.3);
                world.y += wave * _WaveHeight * (1.0 - _Smoke);
                output.positionWS = world;
                output.positionCS = TransformWorldToHClip(world);
                output.wave = wave;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _BaseColor;
                if (_Smoke > 0.0)
                {
                    float t = _Time.y * _WaveSpeed * 0.3;
                    float n = Noise(input.positionWS.xz * 2.0 + float2(t, t * 0.7)) * 0.6
                              + Noise(input.positionWS.xz * 5.0 - float2(t * 1.3, t)) * 0.4;
                    color.a *= lerp(1.0, saturate(n * 1.6 - 0.2), _Smoke);
                }
                else
                {
                    // Wave crests catch a little light.
                    color.rgb += saturate(input.wave) * 0.12;
                }

                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
