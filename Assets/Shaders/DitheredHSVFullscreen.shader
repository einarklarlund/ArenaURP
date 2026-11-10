// Quantizes the camera's image in HSV space with ordered dithering: hue, saturation and value each snap to a fixed
// number of evenly spaced levels, and the Bayer pattern decides per pixel whether a channel rounds down or up.
// A drop-in alternative to Fullscreen/Dithered: put a material with this shader on DitherFeature (Mobile_Renderer).
// It runs after post-processing, on the render-scaled buffer, so one pattern cell covers one low-res pixel.
//
// Hue is circular: _HueSteps levels around the wheel, starting at _HueOffset (0-1, fraction of the wheel), and the
// top band dithers into the bottom one. Saturation and value get their level count spread over 0..1 inclusive, so
// full white, full black and fully saturated colours stay reachable.
Shader "Fullscreen/DitheredHSV"
{
    Properties
    {
        _Pattern ("Pattern", 2D) = "gray" {}
        [IntRange] _HueSteps ("Hue Levels", Range(1, 64)) = 10
        [IntRange] _SaturationSteps ("Saturation Levels", Range(2, 64)) = 20
        [IntRange] _ValueSteps ("Value Levels", Range(2, 64)) = 15
        _HueOffset ("Hue Offset", Range(0, 1)) = 0
        _DitherStrength ("Dither Strength (0 = posterize)", Range(0, 1)) = 1
        [Toggle(_QUANTIZE_IN_SRGB)] _QuantizeInSRGB ("Quantize In sRGB (perceptual)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "DitherHSV"
        HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ _QUANTIZE_IN_SRGB

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_Pattern);
            SAMPLER(sampler_Pattern);
            float4 _Pattern_TexelSize;
            float _HueSteps;
            float _SaturationSteps;
            float _ValueSteps;
            float _HueOffset;
            float _DitherStrength;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 color = saturate(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb);

                // SV_Position is in pixels of the scaled target, so the pattern tiles once per pattern-size pixels
                // whatever the render scale. The _LOD sample skips the render-scale mip bias.
                float2 patternUV = input.positionCS.xy * _Pattern_TexelSize.xy;
                float threshold = SAMPLE_TEXTURE2D_LOD(_Pattern, sampler_Pattern, patternUV, 0).r;
                threshold = lerp(0.5, threshold, _DitherStrength);

                // The buffer is linear; even steps of linear value bunch up visually in the highlights.
                #if _QUANTIZE_IN_SRGB
                    color = LinearToSRGB(color);
                #endif

                float3 hsv = RgbToHsv(color);

                // Scale each channel so levels sit on integers, then round up where the fraction passes the threshold.
                float3 levels = float3(_HueSteps, _SaturationSteps - 1, _ValueSteps - 1);
                float3 scaled = float3(frac(hsv.x - _HueOffset), hsv.yz) * levels;
                float3 quantized = (floor(scaled) + (frac(scaled) > threshold)) / levels;
                hsv = float3(frac(quantized.x + _HueOffset), quantized.yz);

                color = HsvToRgb(hsv);

                #if _QUANTIZE_IN_SRGB
                    color = SRGBToLinear(color);
                #endif

                return half4(color, 1);
            }
        ENDHLSL
        }
    }
}
