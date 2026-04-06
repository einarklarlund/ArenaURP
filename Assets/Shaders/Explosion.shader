Shader "Custom/Explosion"
{
    Properties
    {
        _Progress ("Progress", Range(0, 1)) = 0
        _StartColor ("Start Color", Color) = (1, 0.7, 0.1, 1)
        _EndColor ("End Color", Color) = (1, 0.2, 0, 1)
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Progress;
                half4 _StartColor;
                half4 _EndColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float scale = smoothstep(0, 1, _Progress);
                float3 scaledPos = input.positionOS.xyz * scale;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(scaledPos);
                output.positionCS = vertexInput.positionCS;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half t = smoothstep(0, 1, _Progress);
                half4 col = lerp(_StartColor, _EndColor, t);
                col.a *= 1.0 - smoothstep(0.5, 1.0, t);
                col.rgb = MixFog(col.rgb, input.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
