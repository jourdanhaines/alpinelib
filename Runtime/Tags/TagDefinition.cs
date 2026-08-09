using UnityEngine;

namespace AlpineLib.Tags {
    /// <summary>
    /// Asset that identifies a single tag. Tags are pure identity — a skill, weapon, or modifier
    /// references the same asset, and matching is reference equality against a
    /// <see cref="TagSet"/>, so the tag vocabulary lives in game data rather than in library code.
    /// </summary>
    /// <remarks>
    /// There is deliberately no hierarchy or parent link. Conditional modifiers ("increased Fire
    /// damage with Two Handed weapons") are expressed by listing every required tag on the
    /// modifier's set and testing it for containment in the query context, which stays correct
    /// under any authoring order and costs nothing at runtime beyond a reference compare.
    /// </remarks>
    [CreateAssetMenu(fileName = "TagDefinition", menuName = "AlpineLib/Tags/Tag Definition")]
    public class TagDefinition : ScriptableObject {
        /// <summary>
        /// Human readable name for user interfaces and tooling. The asset itself, not this string,
        /// is the identity used for matching.
        /// </summary>
        public string displayName;
    }
}
