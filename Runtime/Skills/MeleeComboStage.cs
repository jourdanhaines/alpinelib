using System;
using AlpineLib.Body;
using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// How much of the actor's movement one <see cref="MeleeComboStage"/> gives back while it plays.
    /// </summary>
    /// <remarks>
    /// A combo is not uniformly committed: the wind-up of a light swing can be steered, the follow
    /// through of a heavy one must carry the actor forward on its own, and the finisher plants the
    /// feet. This exists only to classify one field of <see cref="MeleeComboStage"/> and has no
    /// meaning apart from it, which is why it is colocated rather than given its own file.
    /// </remarks>
    public enum StageLocomotion {
        /// <summary>
        /// The controller may keep driving the actor from player or AI input for the length of the
        /// stage, normally at a reduced speed. Use for openers that should stay mobile.
        /// </summary>
        Controlled,

        /// <summary>
        /// Input is ignored, but whatever velocity the actor carried into the stage keeps moving it,
        /// scaled by <see cref="MeleeComboStage.momentumSpeedMultiplier"/> and bled off by
        /// <see cref="MeleeComboStage.momentumDrag"/>. Use for swings that should preserve a run-up
        /// without letting the player redirect mid-swing.
        /// </summary>
        Momentum,

        /// <summary>
        /// The actor is pinned for the length of the stage: no input, no carried velocity. Only root
        /// motion can still displace it, and only when <see cref="MeleeComboStage.useRootMotion"/> is
        /// set. This is the committed default.
        /// </summary>
        Locked
    }

    /// <summary>
    /// One swing of a multi-hit melee combo: the clip it triggers, the frames its hit box is live,
    /// what that hit carries, how much the actor may move and turn while it plays, and the window in
    /// which the next press is accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stage is data only. <see cref="SkillSystem"/> owns the runtime decisions made from it, and
    /// the possessing controller owns everything the library cannot see — camera-relative steering,
    /// carried velocity, drag. The stage array lives on <see cref="MeleeSkillDefinition.stages"/>;
    /// payload fields left unset here fall through to the parent
    /// <see cref="SkillDefinition"/> so a three-hit combo only has to author what actually differs
    /// between its swings.
    /// </para>
    /// <para>
    /// All timings are normalized animation time on the stage's own clip, so retiming a clip keeps
    /// the live frames on the swing and the combo window on the recovery. The two override fields use
    /// sentinels rather than nullable types because Unity serializes neither
    /// <see cref="System.Nullable{T}"/> nor a missing object reference distinctly: a null
    /// <see cref="injuryOverride"/> and a negative <see cref="injurySeverityOverride"/> both mean
    /// "inherit from the skill", and there is deliberately no way to author a stage that inflicts no
    /// injury while its parent skill does.
    /// </para>
    /// </remarks>
    [Serializable]
    public class MeleeComboStage {
        /// <summary>
        /// Animator trigger that starts this stage's clip. Empty leaves the animator in whatever state
        /// the previous stage reached, so the stage never begins and the combo hangs until something
        /// cancels it.
        /// </summary>
        [Tooltip("Animator trigger that starts this stage's clip")]
        public string animationTrigger;

        /// <summary>Normalized animation time at which this stage's hit box opens.</summary>
        [Tooltip("Normalized time when the hit box activates")]
        [Range(0f, 1f)] public float damageWindowStart = 0.3f;

        /// <summary>
        /// Normalized animation time at which this stage's hit box closes. A value at or below
        /// <see cref="damageWindowStart"/> leaves the stage with no live frames and it will never hit.
        /// </summary>
        [Tooltip("Normalized time when the hit box deactivates")]
        [Range(0f, 1f)] public float damageWindowEnd = 0.6f;

        /// <summary>
        /// Scales the damage this stage deals, applied after the skill's own damage has been resolved
        /// against the actor's stats. One leaves the skill's damage untouched; a finisher hits harder
        /// by raising it rather than by carrying a second damage number.
        /// </summary>
        [Tooltip("Multiplies the skill's resolved damage for this stage; 1 leaves it unchanged")]
        public float damageMultiplier = 1f;

        /// <summary>
        /// Wound this stage inflicts instead of <see cref="SkillDefinition.injury"/>. Null — the
        /// default — inherits the skill's injury; there is no way to author a stage that inflicts
        /// nothing, because a packet without an injury deals no damage at all.
        /// </summary>
        [Tooltip("Wound this stage inflicts; leave empty to inherit the skill's injury")]
        public InjuryDefinition injuryOverride;

        /// <summary>
        /// Severity this stage inflicts its wound at, replacing
        /// <see cref="SkillDefinition.injurySeverity"/>. Negative — the default — is the inherit
        /// sentinel: any value at or above zero overrides, so zero means "override with no severity"
        /// and is distinct from "inherit".
        /// </summary>
        /// <remarks>
        /// The sentinel exists because Unity serializes a float field unconditionally; an unauthored
        /// stage would otherwise read as severity zero and silently strip bleeding from every hit.
        /// Authored values are expected in the same zero-to-one range as the skill's severity; the
        /// field is deliberately not <c>[Range]</c>-clamped so the negative sentinel remains
        /// reachable in the inspector.
        /// </remarks>
        [Tooltip("Severity override for this stage's wound; negative inherits the skill's severity")]
        public float injurySeverityOverride = -1f;

        /// <summary>How much of the actor's movement this stage gives back while it plays.</summary>
        [Tooltip("Locked pins the actor, Momentum carries its entry velocity, Controlled keeps input")]
        public StageLocomotion locomotion = StageLocomotion.Locked;

        /// <summary>
        /// True when this stage's clip is allowed to move the actor through root motion. Overrides
        /// <see cref="SkillDefinition.useRootMotion"/> per stage, so a combo can plant its opener and
        /// let only its lunging finisher travel.
        /// </summary>
        [Tooltip("Let this stage's animation move the actor through root motion")]
        public bool useRootMotion;

        /// <summary>
        /// Normalized animation time from which a further attack press is buffered as a request to
        /// advance to the next stage. Presses before this point are dropped, which is what stops a
        /// held or mashed button from queueing the whole combo off a single swing.
        /// </summary>
        [Tooltip("Normalized time from which the next attack press is buffered")]
        [Range(0f, 1f)] public float comboWindowStart = 0.2f;

        /// <summary>
        /// Normalized animation time at which a buffered press actually fires the next stage.
        /// </summary>
        /// <remarks>
        /// This must beat the animator's exit transition, which leaves attack states at normalized
        /// time 0.9; a value at or above that lets the state fall back to locomotion first, so the
        /// combo drops its buffered press and reads as an unresponsive second swing. It must also sit
        /// at or after <see cref="comboWindowStart"/>, otherwise no press can ever have been buffered
        /// by the time the advance is evaluated and the combo never leaves its first stage.
        /// </remarks>
        [Tooltip("Normalized time a buffered press advances the combo; must stay below the 0.9 exit")]
        [Range(0f, 1f)] public float comboAdvanceTime = 0.85f;

        /// <summary>
        /// Ceiling in degrees per second on how fast the controller may steer the actor's facing
        /// during this stage, normally toward the camera or movement direction. Zero freezes rotation
        /// outright, committing the swing to the direction it started in.
        /// </summary>
        /// <remarks>
        /// This is a rate cap and is independent of <see cref="SkillDefinition.maxRotation"/>, which
        /// budgets total degrees across the whole skill. A stage that allows fast turning can still be
        /// stopped early by the skill's rotation budget running out.
        /// </remarks>
        [Tooltip("Degrees per second the actor may turn during this stage; 0 freezes facing")]
        public float turnSpeedCap = 360f;

        /// <summary>
        /// Scales the velocity carried into this stage when <see cref="locomotion"/> is
        /// <see cref="StageLocomotion.Momentum"/>. Values above one convert a run-up into a lunge;
        /// values below one damp it. Ignored by the other locomotion modes.
        /// </summary>
        [Tooltip("Scales carried velocity in Momentum stages; ignored otherwise")]
        public float momentumSpeedMultiplier = 1f;

        /// <summary>
        /// Rate at which carried momentum decays during a <see cref="StageLocomotion.Momentum"/>
        /// stage, in per-second exponential units: the carried speed is expected to be scaled by
        /// <c>exp(-momentumDrag * deltaTime)</c> each frame. Zero — the default — carries the entry
        /// velocity unchanged for the whole stage.
        /// </summary>
        /// <remarks>
        /// The library only stores this number. Nothing in <see cref="SkillSystem"/> integrates it,
        /// because the library does not own the actor's velocity: the possessing controller reads the
        /// active stage and applies the decay against whatever motor it drives. A stage authored with
        /// drag but possessed by a controller that ignores it simply slides at full speed rather than
        /// failing loudly.
        /// </remarks>
        [Tooltip("Per-second exponential decay of carried momentum; 0 carries it unchanged")]
        public float momentumDrag;
    }
}
