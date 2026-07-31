using System;

namespace AlpineLib.Stats {
    /// <summary>
    /// How a <see cref="StatModifier"/> combines with the value accumulated before it.
    /// </summary>
    public enum ModifierOperation {
        Flat,
        Percent,
        Multiply
    }

    /// <summary>
    /// A single adjustment to one stat. Serializable so modifiers can be authored on assets and in
    /// inspectors; <see cref="Source"/> is runtime-only and identifies the object that applied the
    /// modifier so it can be removed again through <see cref="StatSheet.RemoveModifiersFrom"/>.
    /// </summary>
    [Serializable]
    public struct StatModifier {
        /// <summary>Stat this modifier adjusts.</summary>
        public StatDefinition Stat;

        /// <summary>How the modifier combines with the running value.</summary>
        public ModifierOperation Operation;

        /// <summary>Operand for the operation: an addend, a fraction, or a factor.</summary>
        public float Value;

        /// <summary>Lower priorities are applied first. Defaults come from the operation.</summary>
        public int Priority;

        /// <summary>
        /// Object that applied this modifier. Runtime only — authored modifiers carry no source and
        /// are re-created with one at apply time.
        /// </summary>
        [NonSerialized] public object Source;

        public StatModifier(StatDefinition stat, ModifierOperation operation, float value, object source, int priority = -1) {
            Stat = stat;
            Operation = operation;
            Value = value;
            Source = source;
            Priority = priority >= 0 ? priority : DefaultPriority(operation);
        }

        private static int DefaultPriority(ModifierOperation operation) {
            return operation switch {
                ModifierOperation.Flat => 100,
                ModifierOperation.Percent => 200,
                ModifierOperation.Multiply => 300,
                _ => 0
            };
        }
    }
}
