// A horizontal frost or ice surface for Cinna's ice trial that reveals itself from a point. Texture coordinates come
// from world XZ, so separate pieces join up without seams. Coverage is 1 inside _RevealRadius of _RevealCenter and
// fades to 0 over _EdgeWidth, with a ragged edge driven by the texture's brightness and a thin glow along the front.
// _RevealDirection 1 shows frost inside the radius (a freeze spreading out); -1 shows it outside (a thaw spreading out).
// Lit with URP's PBR lighting, including Forward+ additional lights and reflection probes.
Shader "Lumenfall/Ice Trial Frost"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Colour", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 0.6
        _Smoothness("Smoothness", Range(0, 1)) = 0.8
        _WorldTiling("Cells Per Texture Repeat", Float) = 3
        _Coverage("Maximum Coverage", Range(0, 1)) = 1
        _RevealCenter("Reveal Centre", Vector) = (0, 0, 0, 0)
        _RevealRadius("Reveal Radius", Float) = 1000
        _RevealDirection("Reveal Direction", Float) = 1
        _EdgeWidth("Edge Width", Float) = 1.2
        _EdgeNoise("Edge Raggedness", Float) = 1.4
        [HDR] _EdgeColor("Edge Glow", Color) = (0.7, 1.1, 1.6, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                half _Smoothness;
                float _WorldTiling;
                half _Coverage;
                float4 _RevealCenter;
                float _RevealRadius;
                float _RevealDirection;
                float _EdgeWidth;
                float _EdgeNoise;
                half4 _EdgeColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionWS.xz / max(_WorldTiling, 0.001);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);

                // The texture's own brightness breaks up the front so it creeps in crystals rather than a clean circle.
                float distanceFromCentre = distance(input.positionWS.xz, _RevealCenter.xz);
                half brightness = dot(albedo.rgb, half3(0.299, 0.587, 0.114));
                float inside = saturate((_RevealRadius - distanceFromCentre + (brightness - 0.5) * _EdgeNoise) / max(_EdgeWidth, 0.001));
                half frost = _RevealDirection >= 0 ? inside : 1.0 - inside;
                half band = 4.0 * inside * (1.0 - inside);
                half alpha = saturate(frost * _Coverage * albedo.a + band * 0.35);
                clip(alpha - 0.002);

                // The surface is horizontal and its UVs follow world X and Z, so tangent space maps straight onto world axes.
                float3 normalWS = normalize(float3(normalTS.x, normalTS.z, normalTS.y));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = _Smoothness;
                surface.normalTS = normalTS;
                surface.occlusion = 1;
                surface.emission = _EdgeColor.rgb * band;
                surface.alpha = alpha;

                half4 colour = UniversalFragmentPBR(inputData, surface);
                colour.rgb = MixFog(colour.rgb, input.fogFactor);
                colour.a = alpha;
                return colour;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
