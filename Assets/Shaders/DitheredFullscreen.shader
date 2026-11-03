// Limits the camera's image to a palette with ordered dithering. Run by DitherFeature on Mobile_Renderer, after
// post-processing, on the render-scaled buffer: one Bayer cell per low-res pixel, which the final blit then upscales
// with point filtering.
// A port of QuickDither's Dithered.shadergraph: _Primary and _Secondary are its 3D palette textures, and a pixel takes
// the primary colour where the primary's alpha (its dither weight) reaches the pattern's threshold, else the secondary.
Shader "Fullscreen/Dithered"
{
    Properties
    {
        _Pattern ("Pattern", 2D) = "gray" {}
        _Primary ("Primary", 3D) = "" {}
        _Secondary ("Secondary", 3D) = "" {}
        _Remap ("Remap (lookup min, max)", Vector) = (0, 1, 0, 0)
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
            Name "Dither"
        HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_Pattern);
            SAMPLER(sampler_Pattern);
            float4 _Pattern_TexelSize;
            TEXTURE3D(_Primary);
            SAMPLER(sampler_Primary);
            TEXTURE3D(_Secondary);
            SAMPLER(sampler_Secondary);
            float4 _Remap;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;

                // SV_Position is in pixels of the scaled target, so the pattern tiles once per pattern-size pixels
                // whatever the render scale. The _LOD samples skip the render-scale mip bias.
                float2 patternUV = input.positionCS.xy * _Pattern_TexelSize.xy;
                half threshold = SAMPLE_TEXTURE2D_LOD(_Pattern, sampler_Pattern, patternUV, 0).r;

                float3 lookup = lerp(_Remap.xxx, _Remap.yyy, saturate(color));
                half4 primary = SAMPLE_TEXTURE3D_LOD(_Primary, sampler_Primary, lookup, 0);
                half3 secondary = SAMPLE_TEXTURE3D_LOD(_Secondary, sampler_Secondary, lookup, 0).rgb;

                return half4(primary.a >= threshold ? primary.rgb : secondary, 1);
            }
        ENDHLSL
        }
    }
}
