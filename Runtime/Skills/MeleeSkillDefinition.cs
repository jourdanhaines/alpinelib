using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// A skill delivered by swinging the actor's <see cref="Combat.HitBox"/> through a timing window
    /// inside its animation. Everything about the payload — damage, injury, tags — comes from
    /// <see cref="SkillDefinition"/>; this subclass only says when the hit box is live.
    /// </summary>
    /// <remarks>
    /// The window is normalized animation time, so retiming the clip keeps the live frames on the
    /// swing. <see cref="SkillSystem"/> opens the hit box once per use and never reopens it after the
    /// window closes, so a single skill activation cannot land two separate flurries even if the
    /// state loops.
    /// </remarks>
    [CreateAssetMenu(fileName = "MeleeSkillDefinition", menuName = "AlpineLib/Skills/Melee Skill")]
    public class MeleeSkillDefinition : SkillDefinition {
        /// <summary>Normalized animation time at which the hit box opens.</summary>
        [Tooltip("Normalized time when the hit box activates")]
        [Range(0f, 1f)] public float damageWindowStart = 0.3f;

        /// <summary>
        /// Normalized animation time at which the hit box closes. A value at or below
        /// <see cref="damageWindowStart"/> leaves the skill with no live frames and it will never hit.
        /// </summary>
        [Tooltip("Normalized time when the hit box deactivates")]
        [Range(0f, 1f)] public float damageWindowEnd = 0.6f;
    }
}
