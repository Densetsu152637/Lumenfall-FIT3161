// One colour of the glitch aura's split image (GlitchAura): an additive, see-through copy of a character's current
// pose, mostly a glowing rim, cut by fine scanlines. Horizontal bands of the copy jump sideways now and then (_Slice)
// and whole bands drop out (_Dropout). _GlitchTime drives both, so the component decides how fast the glitch ticks.
Shader "Lumenfall/Glitch Ghost"
{
    Properties
    {
        _Color("Color", Color) = (1, 0.18, 0.33, 1)
        _Fill("Fill", Range(0, 1)) = 0.12
        _Rim("Rim", Range(0, 4)) = 1.6
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        _GhostOffset("World Offset", Vector) = (0, 0, 0, 0)
        _Slice("Slice Shift", Range(0, 1)) = 0
        _SliceDensity("Slices Per Metre", Float) = 9
        _Dropout("Dropout", Range(0, 1)) = 0
        _LineDensity("Scanlines Per Metre", Float) = 60
        _GlitchTime("Glitch Time", Float) = 0
        _Seed("Seed", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 8
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+50" "IgnoreProjector"="True" }

        Pass
        {
            Name "GlitchGhost"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest [_ZTest]
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fill;
                half _Rim;
                half _RimPower;
                float4 _GhostOffset;
                float _Slice;
                float _SliceDensity;
                float _Dropout;
                float _LineDensity;
                float _GlitchTime;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            float GlitchHash(float n)
            {
                return frac(sin(n * 12.9898 + _Seed * 78.233) * 43758.5453);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + _GhostOffset.xyz;

                // Some horizontal bands slide sideways along the screen for a tick, then snap back.
                float band = floor(positionWS.y * _SliceDensity);
                float tick = floor(_GlitchTime * 14.0);
                float shifted = step(0.72, GlitchHash(band + tick * 17.0));
                float shift = shifted * (GlitchHash(band * 3.1 + tick) - 0.5) * 0.35 * _Slice;
                float3 cameraRight = UNITY_MATRIX_V[0].xyz;
                positionWS += cameraRight * shift;

                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 toCamera = normalize(_WorldSpaceCameraPos - input.positionWS);
                half rim = pow(1.0 - saturate(abs(dot(normal, toCamera))), _RimPower);
                half lines = 0.65 + 0.35 * step(0.5, frac(input.positionWS.y * _LineDensity - _GlitchTime * 2.0));

                float band = floor(input.positionWS.y * _SliceDensity * 0.5);
                float tick = floor(_GlitchTime * 10.0);
                half keep = step(_Dropout, GlitchHash(band * 7.0 + tick * 3.0 + 11.0));

                half strength = (_Fill + rim * _Rim) * lines * keep;
                return half4(_Color.rgb * _Color.a * strength, 1.0);
            }
            ENDHLSL
        }
    }
}
