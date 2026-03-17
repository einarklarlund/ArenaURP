Shader "Custom/LineRendererEdgeFade"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        _FadePower ("Edge Fade Power", Range(0.1, 5)) = 1.0
        _FadeWidth ("Edge Fade Width", Range(0.0, 0.5)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _FadePower;
                half _FadeWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 col = texColor * input.color * _Color;

                // Line renderer UV.y: 0 at one edge, 1 at other, 0.5 at center
                // distFromCenter: 0 at center, 1 at edges
                half distFromCenter = abs(input.uv.y - 0.5) * 2.0;

                // Smooth fade from center to edges
                half fadeStart = 1.0 - _FadeWidth * 2.0;
                half edgeFade = 1.0 - smoothstep(fadeStart, 1.0, distFromCenter);
                edgeFade = pow(edgeFade, _FadePower);

                col.a *= edgeFade;

                col.rgb = MixFog(col.rgb, input.fogFactor);

                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}