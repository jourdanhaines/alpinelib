using System;
using AlpineLib.Tags;

namespace AlpineLib.Stats {
    /// <summary>
    /// How a <see cref="StatModifier"/> combines with the other modifiers on the same stat.
    /// </summary>
    /// <remarks>
    /// The three operations are three independent buckets, not three steps in a chain: flat
    /// addends sum, percentages sum into a single "increased" fraction, and multipliers multiply
    /// together as separate "more" factors. See <see cref="StatSheet"/> for the full expression.
    /// </remarks>
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

        /// <summary>
        /// Retained for compatibility with authored data and no longer affects evaluation order.
        /// </summary>
        /// <remarks>
        /// Bucket math is order-independent — sums and products commute — so there is nothing left
        /// for a priority to sequence. The field stays so existing assets keep deserializing and
        /// the five-argument constructor keeps its shape.
        /// </remarks>
        public int Priority;

        /// <summary>
        /// Condition on this modifier: it contributes only when every one of these tags is present
        /// in the query context. Null or empty means unconditional.
        /// </summary>
        /// <remarks>
        /// This is how "increased Fire damage" is expressed — the modifier carries the Fire tag and
        /// is skipped for any evaluation whose context does not include it. A global stat sheet
        /// read such as <see cref="StatSheet.Get(StatDefinition)"/> uses an empty context, so
        /// conditional modifiers are correctly absent from it.
        /// </remarks>
        public TagSet Tags;

        /// <summary>
        /// Object that applied this modifier. Runtime only — authored modifiers carry no source and
        /// are re-created with one at apply time.
        /// </summary>
        [NonSerialized] public object Source;

        /// <summary>
        /// Creates an unconditional modifier that applies to every evaluation of its stat.
        /// </summary>
        public StatModifier(StatDefinition stat, ModifierOperation operation, float value, object source, int priority = -1) {
            Stat = stat;
            Operation = operation;
            Value = value;
            Source = source;
            Tags = null;
            Priority = priority >= 0 ? priority : DefaultPriority(operation);
        }

        /// <summary>
        /// Creates a modifier that applies only when <paramref name="tags"/> is a subset of the
        /// evaluation context. Pass null for an unconditional modifier.
        /// </summary>
        public StatModifier(StatDefinition stat, ModifierOperation operation, float value, object source, TagSet tags, int priority = -1) {
            Stat = stat;
            Operation = operation;
            Value = value;
            Source = source;
            Tags = tags;
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
