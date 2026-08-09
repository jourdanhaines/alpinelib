using UnityEngine;

namespace AlpineLib.Stats {
    /// <summary>
    /// Authored rule that derives one stat from another: "every point of Strength grants two points
    /// of maximum Health". A <see cref="StatConverter"/> holds a list of these and keeps the target
    /// in step with the source as the source moves.
    /// </summary>
    /// <remarks>
    /// Conversions are data rather than code so a game can retune its attribute derivations, or add
    /// new ones, without a component per rule. The contribution is always flat: it enters the
    /// target's <c>base + sum of Flat</c> bucket, which means "increased" and "more" modifiers on
    /// the target scale the converted points exactly as they scale the authored base.
    /// </remarks>
    [CreateAssetMenu(fileName = "StatConversionDefinition", menuName = "AlpineLib/Stats/Stat Conversion")]
    public class StatConversionDefinition : ScriptableObject {
        /// <summary>Stat that is read. Its current value drives the conversion.</summary>
        public StatDefinition source;

        /// <summary>Stat that receives the converted points as a flat modifier.</summary>
        public StatDefinition target;

        /// <summary>Target points granted per point of source.</summary>
        public float ratio = 1f;
    }
}
