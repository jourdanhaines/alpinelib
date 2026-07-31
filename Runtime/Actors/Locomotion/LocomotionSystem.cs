using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// Gait an actor is currently moving with.
    /// </summary>
    public enum LocomotionState {
        WalkSlow,
        Walk,
        Jog,
        Sprint,
        Crouch,
        CrouchFast
    }

    /// <summary>
    /// Translates the actor's current gait into stat modifiers on its move speed and the noise it
    /// makes. Walk is the neutral gait and applies nothing; every other gait applies multipliers that
    /// are removed again the moment the gait changes or the component is disabled.
    /// </summary>
    [RequireComponent(typeof(Actor))]
    public class LocomotionSystem : MonoBehaviour {
        [Header("Stats")]
        [SerializeField] private StatDefinition moveSpeedStat;
        [SerializeField] private StatDefinition noiseRadiusStat;

        [Header("Speed Multipliers")]
        [SerializeField] private float walkSlowMultiplier = 1f;
        [SerializeField] private float jogMultiplier = 2f;
        [SerializeField] private float sprintMultiplier = 3f;
        [SerializeField] private float crouchMultiplier = 0.4f;
        [SerializeField] private float crouchFastMultiplier = 0.7f;

        [Header("Noise Multipliers")]
        [SerializeField] private float walkSlowNoiseMultiplier = 0.3f;
        [SerializeField] private float jogNoiseMultiplier = 1.5f;
        [SerializeField] private float sprintNoiseMultiplier = 2f;
        [SerializeField] private float crouchNoiseMultiplier = 0.2f;
        [SerializeField] private float crouchFastNoiseMultiplier = 0.4f;

        /// <summary>
        /// Gait currently in effect. Starts at <see cref="LocomotionState.Walk"/>.
        /// </summary>
        public LocomotionState CurrentState { get; private set; } = LocomotionState.Walk;

        private StatSheet _stats;

        private void Start() {
            _stats = GetComponent<StatSheet>();
        }

        /// <summary>
        /// Switches gait, replacing the modifiers applied by the previous one.
        /// </summary>
        public void SetState(LocomotionState state) {
            if (state == CurrentState) return;

            _stats.RemoveModifiersFrom(this);
            CurrentState = state;

            float speedMultiplier = GetSpeedMultiplier(state);
            if (speedMultiplier != 1f) {
                _stats.AddModifier(new StatModifier(
                    moveSpeedStat, ModifierOperation.Multiply, speedMultiplier, this
                ));
            }

            float noiseMultiplier = GetNoiseMultiplier(state);
            if (noiseMultiplier != 1f) {
                _stats.AddModifier(new StatModifier(
                    noiseRadiusStat, ModifierOperation.Multiply, noiseMultiplier, this
                ));
            }
        }

        private float GetSpeedMultiplier(LocomotionState state) {
            return state switch {
                LocomotionState.WalkSlow => walkSlowMultiplier,
                LocomotionState.Walk => 1f,
                LocomotionState.Jog => jogMultiplier,
                LocomotionState.Sprint => sprintMultiplier,
                LocomotionState.Crouch => crouchMultiplier,
                LocomotionState.CrouchFast => crouchFastMultiplier,
                _ => 1f
            };
        }

        private float GetNoiseMultiplier(LocomotionState state) {
            return state switch {
                LocomotionState.WalkSlow => walkSlowNoiseMultiplier,
                LocomotionState.Walk => 1f,
                LocomotionState.Jog => jogNoiseMultiplier,
                LocomotionState.Sprint => sprintNoiseMultiplier,
                LocomotionState.Crouch => crouchNoiseMultiplier,
                LocomotionState.CrouchFast => crouchFastNoiseMultiplier,
                _ => 1f
            };
        }

        private void OnDisable() {
            _stats.RemoveModifiersFrom(this);
            CurrentState = LocomotionState.Walk;
        }
    }
}
