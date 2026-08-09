namespace AlpineLib.Skills {
    /// <summary>
    /// One actor's live copy of a <see cref="SkillDefinition"/>: the shared asset plus the per-actor
    /// state that must not live on it — the cooldown currently running, and who granted the skill.
    /// </summary>
    /// <remarks>
    /// Instances are created by <see cref="SkillSystem"/> and are meaningful only alongside it; the
    /// cooldown is ticked there, which is why the setter is assembly-internal rather than public.
    /// <see cref="Source"/> is compared by reference, so the same definition granted by a weapon and
    /// by a passive produces two independent instances and un-equipping the weapon revokes only its
    /// own.
    /// </remarks>
    public class SkillInstance {
        /// <summary>Shared data this instance was built from. Never null.</summary>
        public SkillDefinition Definition { get; }

        /// <summary>
        /// Whatever granted this skill — a weapon, a passive node, the skill system itself. Used to
        /// revoke grants in bulk through <see cref="SkillSystem.RemoveSkillsFrom"/>.
        /// </summary>
        public object Source { get; }

        /// <summary>
        /// Seconds left before this skill can be used again. Counted down by
        /// <see cref="SkillSystem"/> each frame and reset when the skill finishes.
        /// </summary>
        public float CooldownRemaining { get; internal set; }

        /// <summary>True when the cooldown has elapsed and the skill may be used again.</summary>
        public bool IsReady => CooldownRemaining <= 0f;

        /// <summary>
        /// Creates a ready instance of a skill, off cooldown from the moment it is granted.
        /// </summary>
        /// <param name="definition">Shared data the instance wraps.</param>
        /// <param name="source">Object granting the skill, used later to revoke it.</param>
        public SkillInstance(SkillDefinition definition, object source) {
            Definition = definition;
            Source = source;
        }
    }
}
