using System;
using System.Collections.Generic;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Needs {
    /// <summary>
    /// Side of a <see cref="Threshold"/> value on which the threshold counts as active.
    /// </summary>
    public enum ThresholdDirection {
        Above,
        Below
    }

    /// <summary>
    /// Tone of a <see cref="Threshold"/>, for status icon presentation.
    /// </summary>
    public enum MoodleType {
        Positive,
        Neutral,
        Negative,
        Warning
    }

    /// <summary>
    /// One band of a <see cref="Need"/>'s range. While active it can be surfaced as a status icon
    /// and applies its <see cref="Modifiers"/> to the owner's <see cref="StatSheet"/>, using the
    /// threshold instance itself as the modifier source.
    /// </summary>
    [Serializable]
    public class Threshold {
        /// <summary>Boundary value the need is compared against.</summary>
        public float Value;

        /// <summary>Whether the threshold is active above or below <see cref="Value"/>.</summary>
        public ThresholdDirection ActiveWhen;

        /// <summary>Short display name, e.g. "Hungry".</summary>
        public string Label;

        /// <summary>Long-form flavor text for tooltips.</summary>
        [TextArea] public string Description;

        /// <summary>Tone used when presenting the threshold.</summary>
        public MoodleType MoodleType;

        /// <summary>Stat adjustments applied while the threshold is active. May be null or empty.</summary>
        public StatModifier[] Modifiers;
    }

    /// <summary>
    /// A depleting resource such as health, hunger, or fatigue. Decays over time, raises
    /// <see cref="OnDepleted"/> once when it reaches zero, and tracks which authored
    /// <see cref="Threshold"/> bands are active, applying their stat modifiers to a
    /// <see cref="StatSheet"/> on the same object while they hold.
    /// </summary>
    public abstract class Need : MonoBehaviour {
        [SerializeField] protected float maxValue = 100f;
        [SerializeField] protected float startValue = 100f;
        [SerializeField] protected float decayRate;

        /// <summary>Current amount, between zero and <see cref="MaxValue"/>.</summary>
        public float CurrentValue { get; protected set; }

        /// <summary>Authored ceiling for this need.</summary>
        public float MaxValue => maxValue;

        /// <summary>Current amount as a zero-to-one fraction of <see cref="MaxValue"/>.</summary>
        public float NormalizedValue => CurrentValue / maxValue;

        /// <summary>
        /// The active threshold furthest past its boundary, or null when none is active.
        /// </summary>
        public Threshold ActiveThreshold { get; private set; }

        /// <summary>
        /// Raised once when the need reaches zero. Latched until <see cref="Recover"/> raises the
        /// value above zero again.
        /// </summary>
        public event Action OnDepleted;

        /// <summary>Raised when a threshold becomes active.</summary>
        public event Action<Threshold> OnThresholdEntered;

        /// <summary>Raised when a threshold stops being active.</summary>
        public event Action<Threshold> OnThresholdExited;

        private StatSheet _stats;
        private readonly List<Threshold> _activeThresholds = new();
        private bool _isDepleted;

        /// <summary>
        /// Thresholds evaluated every frame. Implementations typically return a cached array.
        /// </summary>
        protected abstract Threshold[] GetThresholds();

        protected virtual void Start() {
            _stats = GetComponent<StatSheet>();
            CurrentValue = startValue;
        }

        protected virtual void Update() {
            if (_isDepleted) return;

            if (decayRate > 0f) {
                CurrentValue -= decayRate * Time.deltaTime;
                CurrentValue = Mathf.Clamp(CurrentValue, 0f, maxValue);
            }

            EvaluateThresholds();
            CheckDepletion();
        }

        /// <summary>
        /// Reduces the need, clamped at zero.
        /// </summary>
        /// <remarks>
        /// Draining to zero raises <see cref="OnDepleted"/> exactly as passive decay does, so a need
        /// emptied by damage depletes immediately instead of waiting for the next decay tick.
        /// </remarks>
        public void Drain(float amount) {
            CurrentValue = Mathf.Max(0f, CurrentValue - amount);
            CheckDepletion();
        }

        /// <summary>
        /// Restores the need, clamped at <see cref="MaxValue"/>, and clears the depletion latch once
        /// the value is above zero.
        /// </summary>
        public void Recover(float amount) {
            CurrentValue = Mathf.Min(maxValue, CurrentValue + amount);
            if (CurrentValue > 0f) _isDepleted = false;
        }

        private void CheckDepletion() {
            if (_isDepleted) return;
            if (CurrentValue > 0f) return;

            _isDepleted = true;
            OnDepleted?.Invoke();
        }

        private void EvaluateThresholds() {
            var thresholds = GetThresholds();
            Threshold mostExtreme = null;
            float mostExtremeDistance = 0f;

            foreach (var threshold in thresholds) {
                bool isActive = threshold.ActiveWhen switch {
                    ThresholdDirection.Above => CurrentValue >= threshold.Value,
                    ThresholdDirection.Below => CurrentValue <= threshold.Value,
                    _ => false
                };

                bool wasActive = _activeThresholds.Contains(threshold);

                if (isActive && !wasActive) {
                    _activeThresholds.Add(threshold);
                    ApplyModifiers(threshold);
                    OnThresholdEntered?.Invoke(threshold);
                } else if (!isActive && wasActive) {
                    _activeThresholds.Remove(threshold);
                    RemoveModifiers(threshold);
                    OnThresholdExited?.Invoke(threshold);
                }

                if (isActive) {
                    float distance = threshold.ActiveWhen switch {
                        ThresholdDirection.Above => CurrentValue - threshold.Value,
                        ThresholdDirection.Below => threshold.Value - CurrentValue,
                        _ => 0f
                    };

                    if (mostExtreme == null || distance > mostExtremeDistance) {
                        mostExtreme = threshold;
                        mostExtremeDistance = distance;
                    }
                }
            }

            ActiveThreshold = mostExtreme;
        }

        private void ApplyModifiers(Threshold threshold) {
            if (threshold.Modifiers == null) return;
            foreach (var modifier in threshold.Modifiers) {
                var sourced = new StatModifier(modifier.Stat, modifier.Operation, modifier.Value, threshold, modifier.Priority);
                _stats.AddModifier(sourced);
            }
        }

        private void RemoveModifiers(Threshold threshold) {
            if (threshold.Modifiers == null) return;
            _stats.RemoveModifiersFrom(threshold);
        }
    }
}
