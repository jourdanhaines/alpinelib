using UnityEngine;

namespace AlpineLib.Progression {
    /// <summary>
    /// A branch taken on top of a <see cref="ClassDefinition"/>, granting a second passive tree in
    /// addition to the one the parent class already granted.
    /// </summary>
    /// <remarks>
    /// <see cref="parentClass"/> is authored data, not an enforced constraint: nothing in the library
    /// rejects a specialization applied to a character of another class, because eligibility rules
    /// belong to the game that owns character creation. Games that care should compare
    /// <see cref="parentClass"/> before granting, since a mismatch otherwise silently stacks two
    /// unrelated trees.
    /// </remarks>
    [CreateAssetMenu(fileName = "SpecializationDefinition", menuName = "AlpineLib/Progression/Specialization Definition")]
    public class SpecializationDefinition : ScriptableObject {
        /// <summary>
        /// Human readable name for user interfaces and tooling.
        /// </summary>
        public string displayName;

        /// <summary>
        /// Flavour and rules text shown on specialization selection screens.
        /// </summary>
        [TextArea]
        public string description;

        /// <summary>
        /// Artwork representing this specialization in user interfaces.
        /// </summary>
        public Sprite icon;

        /// <summary>
        /// Class this specialization branches from. Advisory only — see the type remarks.
        /// </summary>
        [Tooltip("Class this specialization branches from")]
        public ClassDefinition parentClass;

        /// <summary>
        /// Passive tree granted on top of the parent class's tree.
        /// </summary>
        [Tooltip("Passive tree granted in addition to the parent class's tree")]
        public PassiveTreeDefinition passiveTree;
    }
}
