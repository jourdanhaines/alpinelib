using AlpineLib.Body;
using AlpineLib.Tags;
using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// Which part of the body a skill's animation claims, and therefore how much of the actor's
    /// movement it takes over while it plays.
    /// </summary>
    /// <remarks>
    /// The distinction is what separates a committed swing from a shot fired on the move. It is
    /// colocated with <see cref="SkillDefinition"/> because it exists only to classify one of its
    /// fields and has no meaning apart from it.
    /// </remarks>
    public enum SkillBodyDomain {
        /// <summary>
        /// Plays on the base animator layer and owns the actor outright: locomotion is suppressed and
        /// the controller is expected to stop feeding movement until the skill ends.
        /// </summary>
        FullBody,

        /// <summary>
        /// Plays on the upper-body layer over an unchanged locomotion pose. The actor keeps walking,
        /// slowed by <see cref="SkillDefinition.castMoveSpeedMultiplier"/> rather than stopped.
        /// </summary>
        UpperBody
    }

    /// <summary>
    /// Shared data for one usable skill: what it is called, what it costs, which animation plays it,
    /// how much of the actor it takes over, and what its hits carry. Concrete subclasses add the
    /// delivery mechanism — <see cref="MeleeSkillDefinition"/> for a hit box swung inside a timing
    /// window, <see cref="ProjectileSkillDefinition"/> for a volley spawned mid-animation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A skill is data only; <see cref="SkillSystem"/> owns every runtime decision made from it. That
    /// keeps the same asset reusable across actors — a zombie and a player can be granted the same
    /// skill and each resolves its damage against its own stats.
    /// </para>
    /// <para>
    /// Timing is expressed in normalized animation time rather than seconds, so retiming a clip keeps
    /// the payload aligned with the swing or the release. The animator must be able to reach a state
    /// tagged "Attack" (full body) or "UpperSkill" (upper body) from
    /// <see cref="animationTrigger"/>, otherwise <see cref="SkillSystem"/> never observes the skill
    /// starting and it hangs until something cancels it.
    /// </para>
    /// <para>
    /// The base class carries no <c>CreateAssetMenu</c>: it is abstract and must never appear as a
    /// creatable asset type.
    /// </para>
    /// </remarks>
    public abstract class SkillDefinition : ScriptableObject {
        /// <summary>Human readable name for user interfaces and tooltips.</summary>
        [Tooltip("Human readable name shown in the UI")]
        public string displayName;

        /// <summary>Long-form flavour and rules text shown alongside the skill in the UI.</summary>
        [Tooltip("Long-form description shown in the UI")]
        [TextArea] public string description;

        /// <summary>Icon representing this skill on hotbars and in menus.</summary>
        [Tooltip("Icon shown on hotbars and in menus")]
        public Sprite icon;

        /// <summary>
        /// What this skill is, for tag-conditional stat queries: damage type, weapon class, delivery.
        /// Every stat lookup the skill makes — damage, cost multiplier — is filtered through this set,
        /// so a "increased Melee damage" modifier reaches a skill tagged Melee and nothing else.
        /// </summary>
        [Tooltip("Tags used to filter which stat modifiers apply to this skill")]
        public TagSet tags;

        /// <summary>
        /// Resources spent to use this skill. All of them are checked before any of them is spent, so
        /// a skill never half-pays. Empty means free.
        /// </summary>
        [Tooltip("Resources spent on use; all are checked before any is spent")]
        public ResourceCost[] costs;

        /// <summary>Seconds before this skill can be used again, measured from when it finishes.</summary>
        [Tooltip("Seconds before this skill can be used again")]
        public float cooldown;

        /// <summary>Animator trigger that starts this skill's animation.</summary>
        [Tooltip("Animator trigger that starts this skill's animation")]
        public string animationTrigger;

        /// <summary>How much of the actor this skill's animation takes over while it plays.</summary>
        [Tooltip("Full body stops the actor; upper body plays over locomotion")]
        public SkillBodyDomain bodyDomain;

        /// <summary>
        /// Move speed multiplier applied while an upper-body skill is active. Ignored by full-body
        /// skills, which stop the actor outright.
        /// </summary>
        [Tooltip("Move speed multiplier while casting; upper body skills only")]
        [Range(0.1f, 1f)] public float castMoveSpeedMultiplier = 0.6f;

        /// <summary>
        /// True when this skill's clip is allowed to move the actor through root motion. Full-body
        /// skills suppress root motion unless this is set, so a lunge opts in and a stationary swing
        /// does not slide.
        /// </summary>
        [Tooltip("Let this skill's animation move the actor through root motion")]
        public bool useRootMotion;

        /// <summary>
        /// Damage the skill contributes on its own, before weapon damage and before stat scaling.
        /// </summary>
        [Tooltip("Damage the skill adds by itself, before weapon damage and stat scaling")]
        public float baseDamage;

        /// <summary>
        /// True when the equipped weapon's damage is added to <see cref="baseDamage"/>. Clear it for
        /// spells that should not care what the caster is holding.
        /// </summary>
        [Tooltip("Add the equipped weapon's damage to this skill's base damage")]
        public bool addWeaponDamage = true;

        /// <summary>Wound this skill's hits inflict. A skill with none deals no damage at all.</summary>
        /// <remarks>
        /// Damage travels with the injury: <see cref="Combat.DamageResolver"/> drops a packet that
        /// carries no injury definition, so leaving this empty makes the skill a whiff rather than a
        /// pure-damage hit.
        /// </remarks>
        [Tooltip("Wound inflicted on hit; required for the skill to deal damage")]
        public InjuryDefinition injury;

        /// <summary>How bad the inflicted wound is, scaling its bleeding and condition onset.</summary>
        [Tooltip("Severity of the inflicted wound")]
        [Range(0f, 1f)] public float injurySeverity = 0.25f;

        /// <summary>
        /// Radius of the noise emitted when the skill starts, in world units. Zero is silent.
        /// </summary>
        [Tooltip("Noise radius emitted when the skill starts; zero is silent")]
        public float noiseRadius = 5f;

        /// <summary>
        /// Maximum degrees the actor may turn over the course of this skill. Controllers read the
        /// remaining budget from <see cref="SkillSystem.RemainingAttackRotation"/> to decide whether
        /// they may keep tracking a moving target mid-animation.
        /// </summary>
        [Tooltip("Maximum degrees the actor can turn during this skill")]
        public float maxRotation = 90f;
    }
}
