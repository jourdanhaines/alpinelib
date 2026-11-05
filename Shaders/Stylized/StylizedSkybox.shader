// Gradient skybox for URP: zenith to horizon to ground with a haze band on the horizon and a sun disc with a
// glow. The sun follows the _AlpineSunDirection global (StylizedLightingGlobals); while that is unset (w 0)
// disc and glow are hidden (_SunDirectionFallback only keeps the maths finite).
Shader "AlpineLib/Stylized/Skybox"
{
    Properties
    {
        _ZenithColor("Zenith", Color) = (0.16, 0.42, 0.85, 1)
        _HorizonColor("Horizon", Color) = (0.62, 0.78, 0.95, 1)
        _GroundColor("Ground", Color) = (0.55, 0.36, 0.26, 1)
        _HazeColor("Haze", Color) = (0.93, 0.78, 0.62, 1)
        _HazeHeight("Haze Height", Range(0.001, 1.0)) = 0.12
        _HazeSharpness("Haze Sharpness", Range(0.1, 16.0)) = 2.0
        _GradientExponent("Gradient Exponent", Range(0.05, 4.0)) = 0.6
        [HDR] _SunColor("Sun", Color) = (4, 3.6, 3, 1)
        _SunSize("Sun Size", Range(0.0, 0.5)) = 0.035
        _SunGlowSize("Sun Glow Size", Range(0.001, 2.0)) = 0.45
        _SunGlowStrength("Sun Glow Strength", Range(0.0, 4.0)) = 0.35
        _SunDirectionFallback("Sun Direction Fallback", Vector) = (0.5, 0.64, -0.58, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "Skybox"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex StylizedSkyboxVertex
            #pragma fragment StylizedSkyboxFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StylizedSkyGradient.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _GroundColor;
                half4 _HazeColor;
                half _HazeHeight;
                half _HazeSharpness;
                half _GradientExponent;
                half4 _SunColor;
                half _SunSize;
                half _SunGlowSize;
                half _SunGlowStrength;
                float4 _SunDirectionFallback;
            CBUFFER_END

            // xyz toward the sun, w 1 when set (StylizedLightingGlobals).
            float4 _AlpineSunDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings StylizedSkyboxVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return output;
            }

            float3 SunDirection()
            {
                float3 fallback = _SunDirectionFallback.xyz;
                float3 chosen = _AlpineSunDirection.w > 0.5 ? _AlpineSunDirection.xyz : fallback;
                return SafeNormalize(chosen);
            }

            half4 StylizedSkyboxFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = SafeNormalize(input.directionWS);
                half3 color = AlpineSkyGradient(direction.y, _ZenithColor.rgb, _HorizonColor.rgb, _GroundColor.rgb, _HazeColor.rgb,
                    _GradientExponent, _HazeHeight, _HazeSharpness);

                float3 sun = SunDirection();
                half distanceToSun = length(direction - sun);
                half disc = 1.0h - smoothstep(_SunSize * 0.85h, max(_SunSize, 1e-4h), distanceToSun);
                half glow = AlpineSunGlow(direction, sun, _SunGlowSize) * _SunGlowStrength;
                half discVisible = _AlpineSunDirection.w > 0.5 ? 1.0h : 0.0h;
                color += _SunColor.rgb * ((disc + glow) * discVisible);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
