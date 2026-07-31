using UnityEngine;

namespace AlpineLib.Stats {
    /// <summary>
    /// Asset that identifies a single stat. Games author one asset per stat and reference them
    /// from <see cref="StatSheet"/> base entries and <see cref="StatModifier"/> instances, so the
    /// stat vocabulary lives in game data rather than in library code.
    /// </summary>
    [CreateAssetMenu(fileName = "StatDefinition", menuName = "AlpineLib/Stats/Stat Definition")]
    public class StatDefinition : ScriptableObject {
        /// <summary>
        /// Human readable name for user interfaces and tooling.
        /// </summary>
        public string displayName;

        /// <summary>
        /// Value a <see cref="StatSheet"/> reports for this stat when it holds no authored base entry.
        /// </summary>
        public float defaultValue;
    }
}
