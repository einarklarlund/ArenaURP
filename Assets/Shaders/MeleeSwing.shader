Shader "Custom/MeleeSwing"
{
    Properties
    {
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        _Color ("Color", Color) = (1, 1, 1, 1)
        _SwingProgress ("Swing Progress", Range(0, 1)) = 0

        [Header(Arc Shape)]
        _SwingStartUV ("Swing Start UV", Vector) = (0.9, 0.5, 0, 0)
        _SwingEndUV ("Swing End UV", Vector) = (0.1, 0.5, 0, 0)
        _InnerRadius ("Inner Radius", Range(0, 1)) = 0.2
        _OuterRadius ("Outer Radius", Range(0, 1)) = 0.9
        _TrailLength ("Trail Length", Range(0.01, 1)) = 0.3

        [Header(Edge Softness)]
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.2)) = 0.05
        _LeadingBrightness ("Leading Edge Brightness", Range(1, 5)) = 2.5
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
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _SwingProgress;
                half4 _SwingStartUV;
                half4 _SwingEndUV;
                half _InnerRadius;
                half _OuterRadius;
                half _TrailLength;
                half _EdgeSoftness;
                half _LeadingBrightness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 startPt = _SwingStartUV.xy;
                float2 endPt = _SwingEndUV.xy;

                // Current leading-edge position along the start->end line
                float2 leadPos = lerp(startPt, endPt, smoothstep(0, 1, _SwingProgress));
                // Trailing edge position
                float trailT = saturate(_SwingProgress - _TrailLength);
                float2 trailPos = lerp(startPt, endPt, smoothstep(0, 1, trailT));

                // Swing direction and perpendicular (for thickness)
                float2 swingDir = normalize(endPt - startPt);
                float2 swingPerp = float2(-swingDir.y, swingDir.x);

                // Project pixel onto the swing line relative to trail start
                float2 toPixel = uv - trailPos;
                float proj = dot(toPixel, swingDir);
                float perp = dot(toPixel, swingPerp);

                // Length of the visible trail segment
                float segLen = length(leadPos - trailPos);

                // Normalized position along the trail (0 = trailing, 1 = leading)
                float alongTrail = proj / max(segLen, 0.0001);

                // Trail mask: pixel must be between trailing and leading edges
                float trailMask = smoothstep(-_EdgeSoftness, _EdgeSoftness, alongTrail)
                                * smoothstep(1.0 + _EdgeSoftness, 1.0 - _EdgeSoftness, alongTrail);

                // Radial (thickness) mask using inner/outer radius as perpendicular distance
                float absDist = abs(perp);
                float radialMask = smoothstep(_InnerRadius - _EdgeSoftness, _InnerRadius + _EdgeSoftness, absDist)
                                 * smoothstep(_OuterRadius + _EdgeSoftness, _OuterRadius - _EdgeSoftness, absDist);

                // Brightness: leading edge is bright, fades toward trail
                float brightness = lerp(1.0, _LeadingBrightness, smoothstep(0, 1, alongTrail));

                // Combine
                float alpha = trailMask * radialMask;
                alpha *= step(0.001, _SwingProgress);

                half4 col = _Color;
                col.rgb *= brightness;
                col.a *= saturate(alpha);

                col.rgb = MixFog(col.rgb, input.fogFactor);

                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
