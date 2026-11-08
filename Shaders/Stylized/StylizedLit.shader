// Stylized lit surface for URP: wrapped, ramped Lambert with a tinted shadow side and a rim, driven by the
// _Alpine* shader globals (StylizedLightingGlobals). All-zero globals render plain Lambert. Property names
// follow URP Lit so materials swap shaders in place.
Shader "AlpineLib/Stylized/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.1
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}
        _RimStrength("Rim Strength", Range(0.0, 2.0)) = 1.0
        _VertexColorStrength("Vertex Colour Strength", Range(0.0, 1.0)) = 1.0
        _FogScale("Fog Distance Scale", Range(0.0, 2.0)) = 1.0
        _FogScaleAlpha("Fog Distance Scale At Vertex Alpha 1", Range(0.0, 2.0)) = 1.0
        _FogScaleFar("Fog Distance Scale Far (ramp end)", Range(0.0, 2.0)) = 1.0
        _FogScaleRamp("Fog Scale Ramp (x start, y end view depth; 0 0 off)", Vector) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2.0

        [Toggle(_VERTEX_COLOR)] _VertexColor("Vertex Colour", Float) = 0.0
        [Toggle(_FLAT_SHADING)] _FlatShading("Flat Shading", Float) = 0.0
        [Toggle(_EMISSION)] _Emission("Emission", Float) = 0.0
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0.0
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [Toggle(_NO_LIGHT_COOKIE)] _NoLightCookie("Ignore Light Cookie", Float) = 0.0
        [Toggle(_FOG_SCALE_BLEND)] _FogScaleBlend("Blend Fog Scale By Vertex Alpha", Float) = 0.0
        [Toggle(_FOG_RADIAL)] _FogRadial("Fog By Radial Distance", Float) = 0.0
        [Toggle(_FOG_SKY)] _FogSky("Fog Toward The Sky Behind", Float) = 0.0
        [Toggle(_DITHER_FADE)] _DitherFade("Dither Fade (per-instance _FadeAmount)", Float) = 0.0
        [HideInInspector] _FadeAmount("Fade Amount", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex StylizedLitPassVertex
            #pragma fragment StylizedLitPassFragment

            // Material keywords
            #pragma shader_feature_local _VERTEX_COLOR
            #pragma shader_feature_local_fragment _FLAT_SHADING
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _NO_LIGHT_COOKIE
            #pragma shader_feature_local _FOG_SCALE_BLEND
            #pragma shader_feature_local_fragment _FOG_RADIAL
            #pragma shader_feature_local_fragment _FOG_SKY
            // Screen-door fade-in; builds ship it only where a committed fade template (DitherFadeTemplates) turns it on.
            #pragma shader_feature_local_fragment _DITHER_FADE

            // Universal Pipeline keywords (URP Simple Lit's set minus SSAO, decals, screen-space irradiance and
            // probe-volume SH: this shader does not read them)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            // GPU instancing. SH from UnityPerDraw, not per-instance arrays: Graphics.RenderMeshInstanced never fills
            // those, so instanced props lost their ambient (zeros on Vulkan, stale values on OpenGL).
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer nolightprobe

            #include "StylizedLitInput.hlsl"
            #include "StylizedLitForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment StylizedShadowPassFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _DITHER_FADE
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            // Directional and punctual shadows apply normal bias differently.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "StylizedLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            // The fade clips after URP's alpha-test sample, so that sample's derivatives see the whole pixel quad.
            half4 StylizedShadowPassFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 shadow = ShadowPassFragment(input);
                ALPINE_DITHER_FADE_CLIP(input.positionCS);
                return shadow;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment StylizedDepthOnlyFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _DITHER_FADE
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "StylizedLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"

            half StylizedDepthOnlyFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half depth = DepthOnlyFragment(input);
                ALPINE_DITHER_FADE_CLIP(input.positionCS);
                return depth;
            }
            ENDHLSL
        }

        // Mesh normals only: flat shading is a forward-pass look, not geometry.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment StylizedDepthNormalsFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _DITHER_FADE
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #include "StylizedLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"

            void StylizedDepthNormalsFragment(
                Varyings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #ifdef _WRITE_RENDERING_LAYERS
                DepthNormalsFragment(input, outNormalWS, outRenderingLayers);
            #else
                DepthNormalsFragment(input, outNormalWS);
            #endif
                ALPINE_DITHER_FADE_CLIP(input.positionCS);
            }
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
