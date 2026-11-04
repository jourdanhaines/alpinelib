// Splats one soft disc per cloud puff into the cloud shadow cookie (R8): min-blended over a white clear, darkest
// (_ShadowLight) at the disc's core, softening over its outer _Softness, and fading to none over the cookie's outer
// _EdgeShare so the cookie's edge never shows. Drawn instanced with identity view and clip-space matrices.
Shader "AlpineLib/Sky/CloudShadowSplat" {
    Properties {
        _ShadowLight("Light Under A Cloud", Range(0.0, 1.0)) = 0.55
        _EdgeShare("Cookie Edge Fade Share", Range(0.01, 1.0)) = 0.15
        _Softness("Soft Rim Share", Range(0.01, 1.0)) = 0.6
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass {
            Name "Splat"
            ZWrite Off
            ZTest Always
            Cull Off
            BlendOp Min
            Blend One One
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _ShadowLight;
                half _EdgeShare;
                half _Softness;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;
                float2 cookie : TEXCOORD1;
            };

            Varyings Vert(Attributes input) {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
                output.local = input.positionOS.xy;
                output.cookie = positionWS.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target {
                half disc = 1.0h - smoothstep(1.0h - _Softness, 1.0h, (half)length(input.local));
                half edge = saturate((1.0h - (half)max(abs(input.cookie.x), abs(input.cookie.y))) / _EdgeShare);
                half light = lerp(1.0h, _ShadowLight, disc * edge);
                return half4(light, light, light, 1.0h);
            }
            ENDHLSL
        }
    }
}
