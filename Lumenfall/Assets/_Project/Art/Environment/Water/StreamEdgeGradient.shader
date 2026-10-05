Shader "Lumenfall/Environment/Stream Edge Gradient"
{
    Properties
    {
        _BaseColor ("Water Color", Color) = (0.16, 0.58, 0.78, 0.72)
        _CenterAlpha ("Center Alpha", Range(0, 1)) = 0.72
        _EdgeAlpha ("Edge Alpha", Range(0, 1)) = 0.06
        _FlowSpeed ("Flow Speed", Vector) = (0.035, 0.12, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half _CenterAlpha; half _EdgeAlpha; float4 _FlowSpeed;
            CBUFFER_END
            Varyings vert(Attributes input) { Varyings o; o.positionHCS = TransformObjectToHClip(input.positionOS.xyz); o.uv = input.uv; return o; }
            half4 frag(Varyings input) : SV_Target
            {
                half center = 1.0h - abs(input.uv.y * 2.0h - 1.0h);
                center = smoothstep(0.0h, 0.32h, center);
                half alpha = lerp(_EdgeAlpha, _CenterAlpha, center);
                half flow = sin(input.uv.x * 18.0h + _Time.y * (_FlowSpeed.x + _FlowSpeed.y) * 8.0h) * 0.035h;
                return half4(_BaseColor.rgb + flow, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
