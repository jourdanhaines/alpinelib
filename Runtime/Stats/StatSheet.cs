using System;
using System.Collections.Generic;
using AlpineLib.Tags;
using UnityEngine;

namespace AlpineLib.Stats {
    /// <summary>
    /// Authored base value for one stat on a <see cref="StatSheet"/>.
    /// </summary>
    [Serializable]
    public class StatEntry {
        public StatDefinition stat;
        public float value;
    }

    /// <summary>
    /// Holds an object's base stat values, accumulates <see cref="StatModifier"/> instances, and
    /// resolves final values by folding those modifiers into three buckets.
    /// </summary>
    /// <remarks>
    /// The value of a stat is <c>(base + sum of Flat) * max(0, 1 + sum of Percent) * product of
    /// Multiply</c>. Percent is the additive "increased" bucket — ten separate 10% modifiers make
    /// 2x, not 2.59x — and Multiply is the multiplicative "more" bucket, where each modifier
    /// compounds on its own. The percent factor is floored at zero so stacked penalties bottom out
    /// instead of flipping the sign of the stat.
    /// <para>
    /// Because sums and products commute, evaluation is order-independent and modifiers are never
    /// sorted. A modifier contributes only when its <see cref="StatModifier.Tags"/> are a subset of
    /// the evaluation context; the cached <see cref="Get(StatDefinition)"/> path uses an empty
    /// context, so it reports the unconditional value. Conditional reads — a damage calculation
    /// that knows the hit is Fire and Melee — go through <see cref="Get(StatDefinition, TagSet)"/>
    /// or <see cref="Evaluate"/> and are computed on demand rather than cached, since the context
    /// is not part of the cache key.
    /// </para>
    /// </remarks>
    public class StatSheet : MonoBehaviour {
        [Header("Base Stats")]
        [SerializeField] private List<StatEntry> baseStats = new();

        private readonly Dictionary<StatDefinition, float> _baseStats = new();
        private readonly List<StatModifier> _modifiers = new();
        private readonly Dictionary<StatDefinition, float> _cache = new();
        private readonly List<StatDefinition> _recalculateKeys = new();
        private bool _isDirty = true;

        /// <summary>
        /// Raised after any change to the sheet: a modifier added or removed, or a base value set.
        /// </summary>
        /// <remarks>
        /// Evaluation is lazy — a change only marks the cache dirty — so a handler may read stats
        /// and mutate the sheet again from inside this event without recursion hazards beyond the
        /// re-entrant raise it triggers itself. Reactive consumers such as
        /// <see cref="StatConverter"/> are expected to guard against their own writes.
        /// </remarks>
        public event Action OnChanged;

        /// <summary>
        /// Every modifier currently on this sheet, in the order it was added.
        /// </summary>
        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        private void Awake() {
            SyncBaseStats();
        }

        private void OnValidate() {
            SyncBaseStats();
        }

        /// <summary>
        /// Returns the stat's unconditional value: every untagged modifier applied, every
        /// conditional one skipped. Served from a cache rebuilt on first read after a change.
        /// </summary>
        public float Get(StatDefinition stat) {
            if (_isDirty) Recalculate();

            return _cache.TryGetValue(stat, out float value) ? value : GetBase(stat);
        }

        /// <summary>
        /// Returns the stat's value for a specific context, including the conditional modifiers
        /// whose tags that context satisfies. Computed on demand and never cached.
        /// </summary>
        public float Get(StatDefinition stat, TagSet context) {
            return Fold(stat, context, GetBase(stat));
        }

        /// <summary>
        /// Same fold as <see cref="Get(StatDefinition, TagSet)"/> but against a caller-supplied
        /// base value instead of the sheet's own.
        /// </summary>
        /// <remarks>
        /// This is the damage calculation entry point: the base is the weapon or skill's own
        /// number, and the sheet contributes only the modifiers the hit's tags select. Passing the
        /// base in rather than storing it keeps per-hit values out of the sheet entirely.
        /// </remarks>
        public float Evaluate(StatDefinition stat, TagSet context, float baseOverride) {
            return Fold(stat, context, baseOverride);
        }

        /// <summary>
        /// Returns the stat's unmodified base value, falling back to the definition's default.
        /// </summary>
        public float GetBase(StatDefinition stat) {
            return _baseStats.TryGetValue(stat, out float value) ? value : stat.defaultValue;
        }

        /// <summary>
        /// Overrides the stat's base value at runtime and raises <see cref="OnChanged"/>.
        /// </summary>
        public void SetBase(StatDefinition stat, float value) {
            _baseStats[stat] = value;
            _isDirty = true;

            OnChanged?.Invoke();
        }

        /// <summary>
        /// Adds a modifier to the sheet and raises <see cref="OnChanged"/>.
        /// </summary>
        public void AddModifier(StatModifier modifier) {
            _modifiers.Add(modifier);
            _isDirty = true;

            OnChanged?.Invoke();
        }

        /// <summary>
        /// Removes every modifier applied by the given source object, raising
        /// <see cref="OnChanged"/> only when something was actually removed.
        /// </summary>
        public void RemoveModifiersFrom(object source) {
            int removed = _modifiers.RemoveAll((modifier) => modifier.Source == source);
            if (removed == 0) return;

            _isDirty = true;
            OnChanged?.Invoke();
        }

        private float Fold(StatDefinition stat, TagSet context, float baseValue) {
            float sumFlat = 0f;
            float sumPercent = 0f;
            float productMultiply = 1f;

            foreach (var modifier in _modifiers) {
                if (modifier.Stat != stat) continue;
                if (!TagSet.Matches(modifier.Tags, context)) continue;

                switch (modifier.Operation) {
                    case ModifierOperation.Flat:
                        sumFlat += modifier.Value;
                        break;
                    case ModifierOperation.Percent:
                        sumPercent += modifier.Value;
                        break;
                    case ModifierOperation.Multiply:
                        productMultiply *= modifier.Value;
                        break;
                }
            }

            return (baseValue + sumFlat) * Mathf.Max(0f, 1f + sumPercent) * productMultiply;
        }

        private void SyncBaseStats() {
            _baseStats.Clear();

            foreach (var entry in baseStats) {
                if (entry == null || entry.stat == null) continue;
                _baseStats[entry.stat] = entry.value;
            }

            _isDirty = true;
        }

        private void Recalculate() {
            _cache.Clear();

            CollectStats();

            foreach (var stat in _recalculateKeys) {
                _cache[stat] = Fold(stat, TagSet.Empty, GetBase(stat));
            }

            _isDirty = false;
        }

        private void CollectStats() {
            _recalculateKeys.Clear();

            foreach (var stat in _baseStats.Keys) {
                _recalculateKeys.Add(stat);
            }

            foreach (var modifier in _modifiers) {
                if (modifier.Stat == null) continue;
                if (_baseStats.ContainsKey(modifier.Stat)) continue;
                if (_recalculateKeys.Contains(modifier.Stat)) continue;
                _recalculateKeys.Add(modifier.Stat);
            }
        }
    }
}
