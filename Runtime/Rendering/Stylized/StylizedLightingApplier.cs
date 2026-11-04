using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Applies a <see cref="StylizedLightingPreset"/> to its scene on <c>Awake</c>: poses the sun, writes ambient,
    /// fog, sky and style globals, and puts the preset's post profile on the global volume.
    /// </summary>
    /// <remarks>
    /// Edit mode is not touched (bake a scene with <c>AlpineLib/Rendering/Apply Lighting Preset To Open Scene</c>).
    /// Whoever owns the view distance calls <see cref="ApplyFogEnd"/>; the fog end then survives re-applies.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StylizedLightingApplier : MonoBehaviour {
        [SerializeField] private StylizedLightingPreset preset;
        [Tooltip("The scene's directional sun.")]
        [SerializeField] private Light sun;
        [Tooltip("Global volume that receives the preset's post profile; optional.")]
        [SerializeField] private Volume globalVolume;

        private float? _fogEnd;

        /// <summary>The applier that woke last and is still alive, or null.</summary>
        public static StylizedLightingApplier Active { get; private set; }

        public StylizedLightingPreset Preset => preset;
        public Light Sun => sun;
        public Volume GlobalVolume => globalVolume;

        /// <summary>Applies the preset now (sun, ambient, fog, sky, globals, volume).</summary>
        public void Apply() {
            if (preset == null) {
                Debug.LogWarning($"StylizedLightingApplier::Apply->'{name}' has no preset.", this);
                return;
            }

            if (globalVolume != null && preset.VolumeProfile != null) globalVolume.sharedProfile = preset.VolumeProfile;
            preset.Apply(sun, _fogEnd);
        }

        /// <summary>Sets the fog end (metres) for this and every later apply.</summary>
        public void ApplyFogEnd(float fogEnd) {
            _fogEnd = fogEnd;
            if (preset != null) preset.ApplyFog(fogEnd);
        }

        private void Awake() {
            Active = this;
            Apply();
        }

        private void OnDestroy() {
            if (Active == this) Active = null;
        }

        private void OnValidate() {
            if (!Application.isPlaying || !isActiveAndEnabled) return;

            Apply();
        }
    }
}
