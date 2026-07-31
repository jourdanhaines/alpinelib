using System;
using UnityEngine;

namespace AlpineLib.Body {
    /// <summary>
    /// Authored template for a timed condition a wound can develop — an infection that worsens while
    /// untreated, a poison working its way through, a virus taking hold. Rolled once when the injury
    /// is created and then advanced by it.
    /// </summary>
    /// <remarks>
    /// Instances are shared: every <see cref="Injury"/> created from the same
    /// <see cref="InjuryDefinition"/> points at these same conditions, so no progress or onset state
    /// may be stored here. Runtime state lives on <see cref="Injury.ConditionState"/>.
    /// </remarks>
    [Serializable]
    public class InjuryCondition {
        /// <summary>
        /// Human readable name, shown in tooling and user interfaces.
        /// </summary>
        public string name;

        /// <summary>
        /// Probability that this condition sets in when the injury is applied.
        /// </summary>
        [Range(0f, 1f)] public float onsetChance;

        /// <summary>
        /// Progress gained per second once the condition has set in, before the injury's severity
        /// scales it. Progress runs from zero to one.
        /// </summary>
        public float progressRate = 0.01f;

        /// <summary>
        /// Colour of this condition's progress bar in the body system inspector.
        /// </summary>
        public Color editorColor = Color.white;
    }
}
