using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Progression {
    /// <summary>
    /// One allocatable point on a passive tree: a named bundle of <see cref="StatModifier"/>s that a
    /// <see cref="ProgressionSystem"/> applies to an actor while the node is granted.
    /// </summary>
    /// <remarks>
    /// The asset is pure data and holds no allocation state — whether a node is taken is a property of
    /// the actor that granted it, never of the shared definition. Authored modifiers carry no
    /// <c>Source</c>; the granting system re-creates each one against a per-node source key so that
    /// revoking a single node cannot strip modifiers contributed by another node or by equipment.
    /// </remarks>
    [CreateAssetMenu(fileName = "PassiveNodeDefinition", menuName = "AlpineLib/Progression/Passive Node")]
    public class PassiveNodeDefinition : ScriptableObject {
        /// <summary>
        /// Human readable name for user interfaces and tooling.
        /// </summary>
        public string displayName;

        /// <summary>
        /// Flavour and rules text shown when the node is inspected in a tree view.
        /// </summary>
        [TextArea]
        public string description;

        /// <summary>
        /// Stat adjustments applied for as long as this node is granted.
        /// </summary>
        /// <remarks>
        /// A modifier whose <c>Tags</c> set is non-empty only applies to queries whose context contains
        /// all of those tags, which is how conditional nodes ("increased damage with bows") are
        /// authored without any node-specific code.
        /// </remarks>
        [Tooltip("Stat adjustments applied while this node is granted")]
        public StatModifier[] modifiers;
    }
}
