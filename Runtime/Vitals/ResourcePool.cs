using System;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Vitals {
    /// <summary>
    /// One live resource on an object: a current amount below a ceiling, refilled over time and
    /// spent or damaged through explicit calls. Capacity and regeneration rate come from the
    /// <see cref="ResourceDefinition"/>, optionally resolved against a <see cref="StatSheet"/> on
    /// the same object so buffs and injuries move the pool without any extra wiring.
    /// </summary>
    /// <remarks>
    /// Every change to <see cref="CurrentValue"/> goes through <see cref="Drain"/>,
    /// <see cref="Spend"/> or <see cref="Restore"/>; nothing writes the value directly. That keeps
    /// mutation on a small set of methods a future network layer can gate or replicate.
    /// The initial value is resolved in <c>Start</c>, so anything that contributes capacity
    /// modifiers must register them by <c>Awake</c> to be counted in the starting fill.
    /// </remarks>
    public class ResourcePool : MonoBehaviour {
        [Header("Resource")]
        [Tooltip("Data this pool is built from")]
        [SerializeField] private ResourceDefinition definition;

        private StatSheet _stats;
        private float _maxValue;
        private float _regenDelayRemaining;
        private bool _isDepleted;

        /// <summary>Data this pool was built from.</summary>
        public ResourceDefinition Definition => definition;

        /// <summary>Current amount, between zero and <see cref="MaxValue"/>.</summary>
        public float CurrentValue { get; private set; }

        /// <summary>Ceiling for this pool, refreshed every frame from the bound capacity stat.</summary>
        public float MaxValue => _maxValue;

        /// <summary>Current amount as a zero-to-one fraction of <see cref="MaxValue"/>.</summary>
        public float NormalizedValue => _maxValue <= 0f ? 0f : CurrentValue / _maxValue;

        /// <summary>Seconds left before regeneration resumes. Zero while the pool is regenerating.</summary>
        public float RegenDelayRemaining => _regenDelayRemaining;

        /// <summary>
        /// Raised whenever the amount or the ceiling changes, with the current amount followed by
        /// the ceiling.
        /// </summary>
        public event Action<float, float> OnChanged;

        /// <summary>
        /// Raised once when the pool reaches zero. Latched until the value rises above zero again.
        /// </summary>
        public event Action OnDepleted;

        /// <summary>Raised when the pool reaches its ceiling after having been below it.</summary>
        public event Action OnRefilled;

        private void Start() {
            if (definition == null) {
                Debug.LogError($"{nameof(ResourcePool)} on '{name}' has no {nameof(ResourceDefinition)} assigned.", this);
                enabled = false;
                return;
            }

            _stats = GetComponent<StatSheet>();
            _maxValue = ResolveStatValue(definition.maxValueStat, definition.baseMaxValue);
            CurrentValue = definition.startsFull ? _maxValue : 0f;
            _isDepleted = CurrentValue <= 0f;

            OnChanged?.Invoke(CurrentValue, _maxValue);
        }

        private void Update() {
            RefreshMaxValue();
            TickRegenDelay();
            TickRegen();
        }

        /// <summary>
        /// Removes an amount as damage, clamped at zero. Restarts the regeneration delay, so a pool
        /// under sustained fire never recharges, and raises <see cref="OnDepleted"/> the first time
        /// it empties.
        /// </summary>
        public void Drain(float amount) {
            if (amount <= 0f) return;

            CurrentValue = Mathf.Max(0f, CurrentValue - amount);
            _regenDelayRemaining = definition.regenDelaySeconds;

            OnChanged?.Invoke(CurrentValue, _maxValue);
            CheckDepletion();
        }

        /// <summary>
        /// Pays a cost out of the pool, all or nothing: the call fails and changes nothing when the
        /// pool holds less than the amount asked for. Unlike <see cref="Drain"/> this does not
        /// restart the regeneration delay, because spending is not damage.
        /// </summary>
        /// <returns>True when the cost was paid.</returns>
        public bool Spend(float amount) {
            if (amount <= 0f) return true;
            if (CurrentValue < amount) return false;

            CurrentValue -= amount;

            OnChanged?.Invoke(CurrentValue, _maxValue);
            CheckDepletion();
            return true;
        }

        /// <summary>
        /// Adds an amount, clamped at <see cref="MaxValue"/>. Clears the depletion latch once the
        /// value is above zero and raises <see cref="OnRefilled"/> when the ceiling is reached.
        /// </summary>
        public void Restore(float amount) {
            if (amount <= 0f) return;

            float previousValue = CurrentValue;
            CurrentValue = Mathf.Min(_maxValue, CurrentValue + amount);
            if (Mathf.Approximately(previousValue, CurrentValue)) return;

            if (CurrentValue > 0f) _isDepleted = false;

            OnChanged?.Invoke(CurrentValue, _maxValue);
            CheckRefill(previousValue);
        }

        private void RefreshMaxValue() {
            float resolvedMax = ResolveStatValue(definition.maxValueStat, definition.baseMaxValue);
            if (Mathf.Approximately(resolvedMax, _maxValue)) return;

            _maxValue = resolvedMax;
            CurrentValue = Mathf.Min(CurrentValue, _maxValue);

            OnChanged?.Invoke(CurrentValue, _maxValue);
        }

        private void TickRegenDelay() {
            if (_regenDelayRemaining <= 0f) return;

            _regenDelayRemaining = Mathf.Max(0f, _regenDelayRemaining - Time.deltaTime);
        }

        private void TickRegen() {
            if (_regenDelayRemaining > 0f) return;
            if (CurrentValue >= _maxValue) return;

            float regenPerSecond = ResolveStatValue(definition.regenPerSecondStat, definition.baseRegenPerSecond);
            if (regenPerSecond <= 0f) return;

            Restore(regenPerSecond * Time.deltaTime);
        }

        private float ResolveStatValue(StatDefinition stat, float fallback) {
            if (stat == null) return fallback;
            if (_stats == null) return fallback;

            return _stats.Get(stat);
        }

        private void CheckDepletion() {
            if (_isDepleted) return;
            if (CurrentValue > 0f) return;

            _isDepleted = true;
            OnDepleted?.Invoke();
        }

        private void CheckRefill(float previousValue) {
            if (previousValue >= _maxValue) return;
            if (CurrentValue < _maxValue) return;

            OnRefilled?.Invoke();
        }
    }
}
