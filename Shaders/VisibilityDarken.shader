Shader "AlpineLib/VisibilityDarken" {
    Properties {
        _DarkenTint ("Darken Tint", Color) = (0.02, 0.02, 0.03, 1)
    }
    SubShader {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }

        Pass {
            Name "VisibilityDarken"
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Written every frame by AlpineLib.Perception.Visibility.VisibilityField.
            float4 _AlpineVisibilitySourcePosition;
            float4 _AlpineVisibilitySourceForward;
            float _AlpineVisibilitySourceViewDistance;
            float _AlpineVisibilitySourceViewAngleCosine;
            float _AlpineVisibilitySourceHearingRadius;
            float _AlpineVisibilitySourceEnabled;
            float3 _DarkenTint;

            struct Attributes {
                float4 positionOS : POSITION;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target {
                if (_AlpineVisibilitySourceEnabled < 0.5)
                    return half4(1, 1, 1, 1);

                float2 toPixel = input.positionWS.xz - _AlpineVisibilitySourcePosition.xz;
                float distance = length(toPixel);
                float2 direction = toPixel / max(distance, 0.001);

                // Hearing circle
                float hearingVisibility = 1.0 - smoothstep(_AlpineVisibilitySourceHearingRadius - 0.5, _AlpineVisibilitySourceHearingRadius, distance);

                // View cone
                float2 forward = normalize(_AlpineVisibilitySourceForward.xz);
                float dotProduct = dot(direction, forward);
                float coneAngle = smoothstep(_AlpineVisibilitySourceViewAngleCosine - 0.05, _AlpineVisibilitySourceViewAngleCosine + 0.05, dotProduct);
                float coneRange = 1.0 - smoothstep(_AlpineVisibilitySourceViewDistance - 1.0, _AlpineVisibilitySourceViewDistance, distance);
                float coneVisibility = coneAngle * coneRange;

                float visibility = max(hearingVisibility, coneVisibility);

                // Multiply blend: white = no change, darkenTint = darkened
                half3 tint = lerp(half3(_DarkenTint), half3(1, 1, 1), visibility);
                return half4(tint, 1);
            }
            ENDHLSL
        }
    }
}
