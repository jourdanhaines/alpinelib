using System;
using System.Collections.Generic;
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
    /// resolves final values through a cache that is rebuilt lazily whenever anything changes.
    /// Modifiers are applied in priority order: flat additions, then percentages, then multipliers.
    /// </summary>
    public class StatSheet : MonoBehaviour {
        [Header("Base Stats")]
        [SerializeField] private List<StatEntry> baseStats = new();

        private readonly Dictionary<StatDefinition, float> _baseStats = new();
        private readonly List<StatModifier> _modifiers = new();
        private readonly Dictionary<StatDefinition, float> _cache = new();
        private readonly List<StatDefinition> _recalculateKeys = new();
        private bool _isDirty = true;

        private void Awake() {
            SyncBaseStats();
        }

        private void OnValidate() {
            SyncBaseStats();
        }

        private void SyncBaseStats() {
            _baseStats.Clear();

            foreach (var entry in baseStats) {
                if (entry == null || entry.stat == null) continue;
                _baseStats[entry.stat] = entry.value;
            }

            _isDirty = true;
        }

        /// <summary>
        /// Returns the stat's value with every applicable modifier applied.
        /// </summary>
        public float Get(StatDefinition stat) {
            if (_isDirty) Recalculate();
            return _cache.TryGetValue(stat, out float value) ? value : GetBase(stat);
        }

        /// <summary>
        /// Returns the stat's unmodified base value, falling back to the definition's default.
        /// </summary>
        public float GetBase(StatDefinition stat) {
            return _baseStats.TryGetValue(stat, out float value) ? value : stat.defaultValue;
        }

        /// <summary>
        /// Overrides the stat's base value at runtime.
        /// </summary>
        public void SetBase(StatDefinition stat, float value) {
            _baseStats[stat] = value;
            _isDirty = true;
        }

        public void AddModifier(StatModifier modifier) {
            _modifiers.Add(modifier);
            _isDirty = true;
        }

        /// <summary>
        /// Removes every modifier applied by the given source object.
        /// </summary>
        public void RemoveModifiersFrom(object source) {
            int removed = _modifiers.RemoveAll(m => m.Source == source);
            if (removed > 0) _isDirty = true;
        }

        private void Recalculate() {
            _cache.Clear();
            _modifiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            CollectStats();

            foreach (var stat in _recalculateKeys) {
                float value = GetBase(stat);

                foreach (var modifier in _modifiers) {
                    if (modifier.Stat != stat) continue;

                    value = modifier.Operation switch {
                        ModifierOperation.Flat => value + modifier.Value,
                        ModifierOperation.Percent => value * (1f + modifier.Value),
                        ModifierOperation.Multiply => value * modifier.Value,
                        _ => value
                    };
                }

                _cache[stat] = value;
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
