using UnityEngine;

namespace AlpineLib.Progression {
    /// <summary>
    /// A base character archetype: the identity a character is created as, together with the passive
    /// tree that identity grants.
    /// </summary>
    /// <remarks>
    /// A class owns no runtime state and is never mutated — it is looked up by the systems that build a
    /// character and handed to <see cref="ProgressionSystem.GrantTree"/>. A class with no
    /// <see cref="passiveTree"/> is legal and simply grants nothing, which keeps placeholder classes
    /// authorable before their trees exist.
    /// </remarks>
    [CreateAssetMenu(fileName = "ClassDefinition", menuName = "AlpineLib/Progression/Class Definition")]
    public class ClassDefinition : ScriptableObject {
        /// <summary>
        /// Human readable name for user interfaces and tooling.
        /// </summary>
        public string displayName;

        /// <summary>
        /// Flavour and rules text shown on class selection screens.
        /// </summary>
        [TextArea]
        public string description;

        /// <summary>
        /// Artwork representing this class in user interfaces.
        /// </summary>
        public Sprite icon;

        /// <summary>
        /// Passive tree granted to characters of this class.
        /// </summary>
        [Tooltip("Passive tree granted to characters of this class")]
        public PassiveTreeDefinition passiveTree;
    }
}
