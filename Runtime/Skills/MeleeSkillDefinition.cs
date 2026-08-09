using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// A skill delivered by swinging the actor's <see cref="Combat.HitBox"/> through a timing window
    /// inside its animation, either as a single swing or as a chain of <see cref="MeleeComboStage"/>
    /// swings. Everything about the payload — damage, injury, tags — comes from
    /// <see cref="SkillDefinition"/>; this subclass only says when the hit box is live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is normalized animation time, so retiming the clip keeps the live frames on the
    /// swing. <see cref="SkillSystem"/> opens the hit box once per stage and never reopens it after
    /// that stage's window closes, so a single stage cannot land two separate flurries even if its
    /// animator state loops. Advancing the combo starts a fresh stage and therefore a fresh window;
    /// landing a second hit takes a second stage, not a longer one.
    /// </para>
    /// <para>
    /// A skill with no <see cref="stages"/> behaves exactly as it did before combos existed: one
    /// stage is synthesized from this definition's own trigger and window fields, so existing melee
    /// assets keep working untouched.
    /// </para>
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

        /// <summary>
        /// Ordered swings this skill chains through, each with its own clip, damage window and combo
        /// timing. Empty — the default — makes this a single-stage skill built from the definition's
        /// own <see cref="SkillDefinition.animationTrigger"/>, <see cref="damageWindowStart"/> and
        /// <see cref="damageWindowEnd"/>.
        /// </summary>
        /// <remarks>
        /// When stages are authored, the definition's own trigger and window fields are unused: each
        /// stage carries its own, and a stage whose trigger is empty stalls the chain. Order is the
        /// combo order, and the chain ends after the last entry rather than looping back to the
        /// first.
        /// </remarks>
        [Tooltip("Ordered combo swings; leave empty for a single-stage skill using the fields above")]
        public MeleeComboStage[] stages;
    }
}
