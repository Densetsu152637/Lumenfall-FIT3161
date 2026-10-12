#ifndef LUMENFALL_PLAYER_OCCLUSION_REVEAL_INCLUDED
#define LUMENFALL_PLAYER_OCCLUSION_REVEAL_INCLUDED
float _LFRevealEnabled;
float4x4 _LFRevealVP;
float4 _LFRevealFocus;
float4 _LFRevealForward;
float4 _LFRevealSettings;
float4 _LFRevealPixels;

// Called only by camera visibility passes. ShadowCaster/Meta remain unchanged.
void LumenfallRevealClip(float3 positionWS)
{
    if (_LFRevealEnabled < 0.5) return;
    if (dot(positionWS - _LFRevealFocus.xyz, _LFRevealForward.xyz) >= -_LFRevealForward.w) return;
    float4 projected = mul(_LFRevealVP, float4(positionWS, 1));
    float4 focus = mul(_LFRevealVP, _LFRevealFocus);
    if (projected.w <= 0 || focus.w <= 0) return;
    float2 uv = projected.xy / projected.w * 0.5 + 0.5;
    float2 focusUV = focus.xy / focus.w * 0.5 + 0.5;
    float2 delta = uv - focusUV;
    delta.x *= _LFRevealSettings.w;
    float radius = _LFRevealSettings.x;
    float feather = max(0.0001, _LFRevealSettings.y);
    float opacity = lerp(_LFRevealSettings.z, 1.0,
        smoothstep(max(0.0, radius - feather), radius, length(delta)));
    // Stable screen-space noise avoids transparent sorting and keeps depth consistent.
    float2 pixel = floor(uv * _LFRevealPixels.xy);
    float threshold = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    clip(opacity - max(0.00001, threshold));
}
#endif
