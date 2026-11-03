// The generated level's blended ground (see Arena.LevelGen.TerrainMaterial and TerrainBlendStage). Every triangle
// carries up to three layers in TEXCOORD3 (the same on all three of its vertices) and per-vertex blend weights
// in its colour. The weights interpolate across each height band or sinkhole rim. A pixel samples the layers' slices of the
// albedo and normal arrays (baked by TerrainBake in the Editor), once per distinct layer, and mixes them by weight,
// sharpened by each layer's brightness so the edge breaks up along the texture instead of fading evenly. Lit like Simple
// Lit (Blinn-Phong).
Shader "Arena/Level Terrain"
{
    Properties
    {
        [NoScaleOffset] _AlbedoArray("Albedo array (filled in by the builder)", 2DArray) = "" {}
        [NoScaleOffset] _NormalArray("Normal array (filled in by the builder)", 2DArray) = "" {}
        _BumpScale("Normal strength", Range(0, 2)) = 1
        _BlendContrast("Blend contrast", Range(0, 1)) = 0.7
        _HeightInfluence("Height influence", Range(0, 1)) = 0.5
        _SpecColor("Specular colour", Color) = (0.2, 0.2, 0.2, 1)

        // Per layer, in TerrainLayer order: tiling, tint and smoothness, copied from the layer's own material.
        [HideInInspector] _Layer0_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer1_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer2_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer3_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer4_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer5_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer6_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer7_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer8_ST("", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Layer0Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer1Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer2Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer3Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer4Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer5Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer6Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer7Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer8Color("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Layer0Smoothness("", Float) = 0.5
        [HideInInspector] _Layer1Smoothness("", Float) = 0.5
        [HideInInspector] _Layer2Smoothness("", Float) = 0.5
        [HideInInspector] _Layer3Smoothness("", Float) = 0.5
        [HideInInspector] _Layer4Smoothness("", Float) = 0.5
        [HideInInspector] _Layer5Smoothness("", Float) = 0.5
        [HideInInspector] _Layer6Smoothness("", Float) = 0.5
        [HideInInspector] _Layer7Smoothness("", Float) = 0.5
        [HideInInspector] _Layer8Smoothness("", Float) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half _BumpScale;
        half _BlendContrast;
        half _HeightInfluence;
        half4 _SpecColor;
        float4 _Layer0_ST, _Layer1_ST, _Layer2_ST, _Layer3_ST, _Layer4_ST, _Layer5_ST, _Layer6_ST, _Layer7_ST, _Layer8_ST;
        half4 _Layer0Color, _Layer1Color, _Layer2Color, _Layer3Color, _Layer4Color, _Layer5Color, _Layer6Color, _Layer7Color, _Layer8Color;
        half _Layer0Smoothness, _Layer1Smoothness, _Layer2Smoothness, _Layer3Smoothness, _Layer4Smoothness, _Layer5Smoothness, _Layer6Smoothness, _Layer7Smoothness, _Layer8Smoothness;
    CBUFFER_END

    TEXTURE2D_ARRAY(_AlbedoArray);
    SAMPLER(sampler_AlbedoArray);
    TEXTURE2D_ARRAY(_NormalArray);
    SAMPLER(sampler_NormalArray);
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "SimpleLit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma shader_feature_local _NORMALMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_DBUFFER)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float3 layers : TEXCOORD3;
                half4 weights : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                #ifdef _NORMALMAP
                    half4 tangentWS : TEXCOORD3; // w: the bitangent's sign
                #endif
                nointerpolation float3 layers : TEXCOORD4;
                half3 weights : TEXCOORD5;
                half fogFactor : TEXCOORD6;
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    half3 vertexLight : TEXCOORD7;
                #endif
                half3 vertexSH : TEXCOORD8;
                #ifdef USE_APV_PROBE_OCCLUSION
                    float4 probeOcclusion : TEXCOORD9;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct Layer
            {
                half4 albedo;
                half3 normalTS;
                half smoothness;
            };

            Layer SampleLayer(float2 uv, float index)
            {
                const float4 st[9] = { _Layer0_ST, _Layer1_ST, _Layer2_ST, _Layer3_ST, _Layer4_ST, _Layer5_ST, _Layer6_ST, _Layer7_ST, _Layer8_ST };
                const half4 colors[9] = { _Layer0Color, _Layer1Color, _Layer2Color, _Layer3Color, _Layer4Color, _Layer5Color, _Layer6Color, _Layer7Color, _Layer8Color };
                const half smoothness[9] = { _Layer0Smoothness, _Layer1Smoothness, _Layer2Smoothness, _Layer3Smoothness, _Layer4Smoothness, _Layer5Smoothness, _Layer6Smoothness, _Layer7Smoothness, _Layer8Smoothness };
                int i = clamp((int)round(index), 0, 8);
                float2 tiled = uv * st[i].xy + st[i].zw;
                Layer layer;
                layer.albedo = SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_AlbedoArray, tiled, i) * colors[i];
                #ifdef _NORMALMAP
                    layer.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_NormalArray, tiled, i), _BumpScale);
                #else
                    layer.normalTS = half3(0, 0, 1);
                #endif
                layer.smoothness = smoothness[i];
                return layer;
            }

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = input.uv;
                output.normalWS = normal.normalWS;
                #ifdef _NORMALMAP
                    output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                #endif
                output.layers = input.layers;
                output.weights = input.weights.rgb;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    output.vertexLight = VertexLighting(position.positionWS, normal.normalWS);
                #endif
                OUTPUT_SH4(position.positionWS, output.normalWS, GetWorldSpaceNormalizeViewDir(position.positionWS), output.vertexSH, output.probeOcclusion);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                Layer a = SampleLayer(input.uv, input.layers.x);
                half4 albedo = a.albedo;
                half3 normalTS = a.normalTS;
                half smoothness = a.smoothness;
                [branch] if (any(input.layers.yz != input.layers.xx))
                {
                    // Many blend triangles carry only two layers: sample each distinct layer once.
                    Layer b = a;
                    [branch] if (input.layers.y != input.layers.x) b = SampleLayer(input.uv, input.layers.y);
                    Layer c = b;
                    if (input.layers.z == input.layers.x) c = a;
                    else [branch] if (input.layers.z != input.layers.y) c = SampleLayer(input.uv, input.layers.z);
                    // Height blend: each corner's weight is raised by its texture's brightness, and only what comes
                    // within a contrast-dependent depth of the strongest survives.
                    half3 heights = half3(Luminance(a.albedo.rgb), Luminance(b.albedo.rgb), Luminance(c.albedo.rgb));
                    half3 keys = input.weights + _HeightInfluence * heights;
                    keys = half3(input.weights.x > 0.0h ? keys.x : -2.0h,
                                 input.weights.y > 0.0h ? keys.y : -2.0h,
                                 input.weights.z > 0.0h ? keys.z : -2.0h);
                    half depth = lerp(1.0h, 0.02h, _BlendContrast);
                    half3 w = max(keys - (max(keys.x, max(keys.y, keys.z)) - depth), 0.0h);
                    // A texture's brightness must not introduce a material outside its assigned height band.
                    w *= input.weights;
                    w /= max(w.x + w.y + w.z, 1e-4h);
                    albedo = a.albedo * w.x + b.albedo * w.y + c.albedo * w.z;
                    normalTS = normalize(a.normalTS * w.x + b.normalTS * w.y + c.normalTS * w.z);
                    smoothness = a.smoothness * w.x + b.smoothness * w.y + c.smoothness * w.z;
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                #ifdef _NORMALMAP
                    half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                    inputData.tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS);
                    inputData.normalWS = TransformTangentToWorld(normalTS, inputData.tangentToWorld);
                #else
                    inputData.normalWS = input.normalWS;
                #endif
                inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    inputData.vertexLighting = input.vertexLight;
                #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                    inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS,
                        inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
                #else
                    inputData.bakedGI = SampleSHPixel(input.vertexSH, inputData.normalWS);
                    inputData.shadowMask = half4(1, 1, 1, 1);
                #endif

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb;
                surface.alpha = 1;
                surface.specular = _SpecColor.rgb;
                surface.smoothness = smoothness;
                surface.normalTS = normalTS;
                surface.occlusion = 1;
                #if defined(_DBUFFER)
                    ApplyDecalToSurfaceData(input.positionCS, surface, inputData);
                #endif

                half4 color = UniversalFragmentBlinnPhong(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 Fragment() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half Fragment() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
