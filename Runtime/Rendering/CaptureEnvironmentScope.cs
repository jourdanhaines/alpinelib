using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Swaps the global render environment for a capture and restores it on dispose: fog off, flat
    /// ambient (colour and probe), every scene light off (point and spot lamps too) and an optional key light on.
    /// Captures run between frames, so the main view never sees the swap.
    /// </summary>
    public sealed class CaptureEnvironmentScope : IDisposable {
        private readonly bool _fog;
        private readonly AmbientMode _ambientMode;
        private readonly Color _ambientLight;
        private readonly float _ambientIntensity;
        private readonly SphericalHarmonicsL2 _ambientProbe;
        private readonly List<Light> _disabledLights = new List<Light>();
        private GameObject _keyLight;

        /// <summary>Applies <paramref name="lighting"/>; <paramref name="camera"/> orients the key light.</summary>
        public CaptureEnvironmentScope(CaptureLighting lighting, Camera camera) {
            _fog = RenderSettings.fog;
            _ambientMode = RenderSettings.ambientMode;
            _ambientLight = RenderSettings.ambientLight;
            _ambientIntensity = RenderSettings.ambientIntensity;
            _ambientProbe = RenderSettings.ambientProbe;

            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = lighting.Ambient;
            RenderSettings.ambientIntensity = 1f;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(lighting.Ambient.linear);
            RenderSettings.ambientProbe = probe;

            DisableSceneLights();
            if (lighting.HasKey) _keyLight = CreateKeyLight(lighting, camera);
        }

        /// <summary>Restores everything the constructor changed.</summary>
        public void Dispose() {
            RenderSettings.fog = _fog;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.ambientIntensity = _ambientIntensity;
            RenderSettings.ambientProbe = _ambientProbe;

            foreach (Light light in _disabledLights) {
                if (light != null) light.enabled = true;
            }

            _disabledLights.Clear();
            if (_keyLight != null) Object.DestroyImmediate(_keyLight);
            _keyLight = null;
        }

        private void DisableSceneLights() {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) {
                if (!light.enabled) continue;

                light.enabled = false;
                _disabledLights.Add(light);
            }
        }

        private static GameObject CreateKeyLight(CaptureLighting lighting, Camera camera) {
            var host = new GameObject("CaptureKeyLight") { hideFlags = HideFlags.HideAndDontSave };
            var light = host.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = lighting.KeyColour;
            light.intensity = lighting.KeyIntensity;
            light.shadows = LightShadows.None;
            Quaternion basis = camera != null ? camera.transform.rotation : Quaternion.identity;
            host.transform.rotation = basis * Quaternion.Euler(lighting.KeyEulerDegrees);
            return host;
        }
    }
}
