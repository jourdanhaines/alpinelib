using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Stats {
    /// <summary>
    /// Keeps a set of <see cref="StatConversionDefinition"/> rules applied to the sibling
    /// <see cref="StatSheet"/>, re-deriving each target whenever its source stat moves.
    /// </summary>
    /// <remarks>
    /// This replaces the one-shot derivation adapters games used to write by hand. Those computed
    /// "two Health per Strength" once in <c>Awake</c> and went stale the moment a passive node, a
    /// buff, or an item changed Strength; this component subscribes to
    /// <see cref="StatSheet.OnChanged"/> and revokes and re-applies the affected conversion
    /// instead.
    /// <para>
    /// Execution order 100 puts it after <see cref="StatSheet"/>'s <c>Awake</c>, so base values are
    /// synced before the first read, and still ahead of every <c>Start</c> — resource pools resolve
    /// their starting fill there and must see the converted capacity already on the sheet.
    /// </para>
    /// <para>
    /// Each conversion applies its modifier under its own private source key, so revoking one never
    /// disturbs another. Writes made while re-deriving raise <see cref="StatSheet.OnChanged"/> in
    /// turn; a re-entrancy flag swallows those, which means chained conversions (A feeds B feeds C)
    /// settle one link per external change rather than all at once. Order the list so upstream
    /// conversions come first and a single pass resolves the whole chain.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(StatSheet))]
    public class StatConverter : MonoBehaviour {
        [Tooltip("Derivation rules applied to the sibling stat sheet, upstream sources first")]
        [SerializeField] private StatConversionDefinition[] conversions;

        private StatSheet _stats;
        private object[] _conversionKeys;
        private float[] _lastInputs;
        private bool[] _hasApplied;
        private bool _isApplying;

        /// <summary>
        /// Rules this converter is maintaining, in the order they are evaluated.
        /// </summary>
        public IReadOnlyList<StatConversionDefinition> Conversions =>
            conversions ?? Array.Empty<StatConversionDefinition>();

        private void Awake() {
            _stats = GetComponent<StatSheet>();

            int conversionCount = conversions?.Length ?? 0;
            _conversionKeys = new object[conversionCount];
            _lastInputs = new float[conversionCount];
            _hasApplied = new bool[conversionCount];

            for (int index = 0; index < conversionCount; index++) {
                _conversionKeys[index] = new object();
            }

            RefreshAllConversions();

            _stats.OnChanged += HandleStatsChanged;
        }

        private void OnDestroy() {
            if (_stats == null) return;

            _stats.OnChanged -= HandleStatsChanged;
        }

        private void HandleStatsChanged() {
            if (_isApplying) return;

            RefreshAllConversions();
        }

        private void RefreshAllConversions() {
            _isApplying = true;

            for (int index = 0; index < _conversionKeys.Length; index++) {
                RefreshConversion(index);
            }

            _isApplying = false;
        }

        private void RefreshConversion(int index) {
            var conversion = conversions[index];
            if (conversion == null) return;
            if (conversion.source == null || conversion.target == null) return;

            float input = _stats.Get(conversion.source);
            if (_hasApplied[index] && Mathf.Approximately(input, _lastInputs[index])) return;

            if (_hasApplied[index]) _stats.RemoveModifiersFrom(_conversionKeys[index]);

            _lastInputs[index] = input;
            _hasApplied[index] = true;

            _stats.AddModifier(new StatModifier(conversion.target, ModifierOperation.Flat, input * conversion.ratio, _conversionKeys[index]));
        }
    }
}
