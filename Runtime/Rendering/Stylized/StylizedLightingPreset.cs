using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlpineLib.Rendering {
    /// <summary>
    /// One lighting look: sun, Trilight ambient, linear fog, gradient sky and the stylized shader globals, plus an
    /// optional post profile. <see cref="Apply"/> writes it all into <see cref="RenderSettings"/>.
    /// </summary>
    /// <remarks>
    /// The sky's haze is always the fog colour, so the fogged world melts into the horizon band. In play mode the
    /// skybox is an instanced copy of <see cref="SkyboxMaterial"/>, so applying never dirties the asset; edit mode
    /// draws the asset as baked.
    /// </remarks>
    [CreateAssetMenu(fileName = "StylizedLightingPreset", menuName = "AlpineLib/Rendering/Stylized Lighting Preset")]
    public sealed class StylizedLightingPreset : ScriptableObject {
        [Header("Sun")]
        [Tooltip("Sun rotation in degrees (x is elevation).")]
        [SerializeField] private Vector3 sunEuler = new Vector3(40f, -40f, 0f);
        [SerializeField] private Color sunColor = new Color(1f, 0.89f, 0.72f);
        [SerializeField, Min(0f)] private float sunIntensity = 1.2f;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.8f;
        [Tooltip("URP depth bias in shadow texels; the preset owns it, not the pipeline asset.")]
        [SerializeField, Range(0f, 10f)] private float shadowBias = 0.1f;
        [Tooltip("URP normal bias in shadow texels.")]
        [SerializeField, Range(0f, 10f)] private float shadowNormalBias = 0.5f;

        [Header("Ambient (Trilight)")]
        [SerializeField] private Color ambientSky = new Color(0.52f, 0.64f, 0.86f);
        [SerializeField] private Color ambientEquator = new Color(0.86f, 0.66f, 0.5f);
        [SerializeField] private Color ambientGround = new Color(0.55f, 0.36f, 0.26f);

        [Header("Fog (linear)")]
        [Tooltip("Fog colour; also the sky's haze band.")]
        [SerializeField] private Color fogColor = new Color(0.93f, 0.78f, 0.62f);
        [Tooltip("Fog start as a share of the fog end.")]
        [SerializeField, Range(0f, 1f)] private float fogStartShare = 0.35f;
        [Tooltip("Fog end in metres when the caller gives none.")]
        [SerializeField, Min(1f)] private float fallbackFogEnd = 400f;

        [Header("Sky")]
        [Tooltip("An AlpineLib/Stylized/Skybox material; drawn through an instanced copy.")]
        [SerializeField] private Material skyboxMaterial;
        [SerializeField] private Color skyZenith = new Color(0.16f, 0.42f, 0.85f);
        [SerializeField] private Color skyHorizon = new Color(0.62f, 0.78f, 0.95f);
        [SerializeField] private Color skyGround = new Color(0.55f, 0.36f, 0.26f);
        [SerializeField, Range(0.001f, 1f)] private float hazeHeight = 0.12f;
        [SerializeField, Range(0.1f, 16f)] private float hazeSharpness = 2f;
        [SerializeField, Range(0.05f, 4f)] private float gradientExponent = 0.6f;
        [SerializeField, ColorUsage(false, true)] private Color sunDiscColor = new Color(4f, 3.6f, 3f);
        [SerializeField, Range(0f, 0.5f)] private float sunDiscSize = 0.035f;
        [SerializeField, Range(0.001f, 2f)] private float sunGlowSize = 0.45f;
        [SerializeField, Range(0f, 4f)] private float sunGlowStrength = 0.35f;

        [Header("Style globals")]
        [Tooltip("Shadow-side tint (rgb) and strength (a).")]
        [SerializeField] private Color shadowTint = new Color(0.85f, 0.5f, 0.4f, 0.4f);
        [Tooltip("Rim colour (rgb) and strength (a).")]
        [SerializeField] private Color rimColor = new Color(1f, 0.85f, 0.7f, 0.25f);
        [SerializeField, Range(0f, 1f)] private float wrap = 0.35f;
        [SerializeField, Range(0f, 0.5f)] private float rampSoftness = 0.15f;
        [SerializeField, Range(0.5f, 16f)] private float rimPower = 4f;
        [SerializeField, Range(0f, 1f)] private float metalReflection = 0.6f;

        [Header("Post")]
        [Tooltip("Global post profile the applier puts on its volume; optional.")]
        [SerializeField] private VolumeProfile volumeProfile;

        [NonSerialized] private Material _skyboxInstance;

        public Vector3 SunEuler => sunEuler;
        public Color SunColor => sunColor;
        public float SunIntensity => sunIntensity;
        public Color AmbientSky => ambientSky;
        public Color AmbientEquator => ambientEquator;
        public Color AmbientGround => ambientGround;
        public Color FogColor => fogColor;
        public float FogStartShare => fogStartShare;
        public float FallbackFogEnd => fallbackFogEnd;
        public Material SkyboxMaterial => skyboxMaterial;
        public VolumeProfile VolumeProfile => volumeProfile;

        /// <summary>Direction toward the sun for <see cref="SunEuler"/>.</summary>
        public Vector3 TowardSun => -(Quaternion.Euler(sunEuler) * Vector3.forward);

        /// <summary>
        /// Applies the whole look: <paramref name="sun"/> (optional) is posed and becomes <c>RenderSettings.sun</c>;
        /// fog ends at <paramref name="fogEndOverride"/> or <see cref="FallbackFogEnd"/>.
        /// </summary>
        public void Apply(Light sun, float? fogEndOverride) {
            if (sun != null) ApplySun(sun);
            TrilightProbe.Apply(ambientSky, ambientEquator, ambientGround);
            ApplyFog(fogEndOverride);
            ApplySkybox();
            StylizedLightingGlobals.Apply(shadowTint, rimColor, wrap, rampSoftness, rimPower, metalReflection);
            StylizedLightingGlobals.SetSunDirection(sun != null ? -sun.transform.forward : TowardSun);
            if (Application.isPlaying) DynamicGI.UpdateEnvironment();
        }

        /// <summary>Linear fog in the preset colour ending at <paramref name="fogEndOverride"/> or the fallback; assigns only what differs.</summary>
        public void ApplyFog(float? fogEndOverride) {
            float end = Mathf.Max(1f, fogEndOverride ?? fallbackFogEnd);
            float start = end * fogStartShare;
            if (!RenderSettings.fog) RenderSettings.fog = true;
            if (RenderSettings.fogMode != FogMode.Linear) RenderSettings.fogMode = FogMode.Linear;
            if (!Mathf.Approximately(RenderSettings.fogStartDistance, start)) RenderSettings.fogStartDistance = start;
            if (!Mathf.Approximately(RenderSettings.fogEndDistance, end)) RenderSettings.fogEndDistance = end;
            if (RenderSettings.fogColor != fogColor) RenderSettings.fogColor = fogColor;
        }

        /// <summary>Writes the sky colours and sun shape into <paramref name="material"/> (an asset only when baking on purpose).</summary>
        public void WriteSkybox(Material material) {
            if (material == null) return;

            SetColor(material, StylizedSkyboxShader.ZenithColor, skyZenith);
            SetColor(material, StylizedSkyboxShader.HorizonColor, skyHorizon);
            SetColor(material, StylizedSkyboxShader.GroundColor, skyGround);
            SetColor(material, StylizedSkyboxShader.HazeColor, fogColor);
            SetFloat(material, StylizedSkyboxShader.HazeHeight, hazeHeight);
            SetFloat(material, StylizedSkyboxShader.HazeSharpness, hazeSharpness);
            SetFloat(material, StylizedSkyboxShader.GradientExponent, gradientExponent);
            SetColor(material, StylizedSkyboxShader.SunColor, sunDiscColor);
            SetFloat(material, StylizedSkyboxShader.SunSize, sunDiscSize);
            SetFloat(material, StylizedSkyboxShader.SunGlowSize, sunGlowSize);
            SetFloat(material, StylizedSkyboxShader.SunGlowStrength, sunGlowStrength);
            if (material.HasProperty(StylizedSkyboxShader.SunDirectionFallback)) {
                Vector3 toward = TowardSun;
                material.SetVector(StylizedSkyboxShader.SunDirectionFallback, new Vector4(toward.x, toward.y, toward.z, 0f));
            }
        }

        private void ApplySun(Light sun) {
            sun.transform.rotation = Quaternion.Euler(sunEuler);
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            if (sun.shadows == LightShadows.None) sun.shadows = LightShadows.Soft;
            sun.shadowStrength = shadowStrength;
            sun.shadowBias = shadowBias;
            sun.shadowNormalBias = shadowNormalBias;
            UniversalAdditionalLightData lightData = sun.GetUniversalAdditionalLightData();
            if (lightData != null) lightData.usePipelineSettings = false;
            RenderSettings.sun = sun;
        }

        // Edit mode draws the asset itself (the bake menu writes the values into it), so a saved scene never points at
        // a DontSave copy; play mode draws an instanced copy so the asset stays clean.
        private void ApplySkybox() {
            if (skyboxMaterial == null) return;

            if (!Application.isPlaying) {
                RenderSettings.skybox = skyboxMaterial;
                return;
            }

            if (_skyboxInstance == null || _skyboxInstance.shader != skyboxMaterial.shader) {
                DestroySkyboxInstance();
                _skyboxInstance = new Material(skyboxMaterial) { name = $"{skyboxMaterial.name} ({name})", hideFlags = HideFlags.DontSave };
            } else {
                _skyboxInstance.CopyPropertiesFromMaterial(skyboxMaterial);
            }

            WriteSkybox(_skyboxInstance);
            RenderSettings.skybox = _skyboxInstance;
        }

        // Runs on asset unload, domain reload and shutdown, when the RenderSettings manager may already be gone (an
        // editor exit while playing never raises Application.quitting). Only the instance is freed; a sky that still
        // points at it draws nothing until the next apply.
        private void OnDisable() {
            DestroySkyboxInstance();
        }

        private void DestroySkyboxInstance() {
            if (_skyboxInstance == null) return;

            DestroyImmediate(_skyboxInstance);
            _skyboxInstance = null;
        }

        private static void SetColor(Material material, int property, Color value) {
            if (material.HasProperty(property)) material.SetColor(property, value);
        }

        private static void SetFloat(Material material, int property, float value) {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }
}
