namespace AlpineLib.Combat {
    /// <summary>
    /// Contract a <see cref="HitBox"/> reports its contacts through. Implemented by whatever drives
    /// the hit box — a <see cref="CombatSystem"/> for animator-driven melee, a projectile, a trap,
    /// an environmental hazard.
    /// </summary>
    /// <remarks>
    /// The hit box owns none of the resolution policy: it only de-duplicates targets for the
    /// duration of one activation and hands each new <see cref="HurtBox"/> to its owner. Deciding
    /// whose hit it is, whether it is a friendly-fire hit, what injury it inflicts and how much
    /// damage it carries all belongs to the implementer, which usually forwards a
    /// <see cref="DamagePacket"/> to <see cref="DamageResolver.Apply"/>.
    /// </remarks>
    public interface IHitBoxOwner {
        /// <summary>
        /// Called once per hurt box the owner's live damage window overlaps, at most once per hurt
        /// box per activation.
        /// </summary>
        /// <param name="hurtBox">Damageable region that was struck. Never null.</param>
        /// <remarks>
        /// Called from <see cref="HitBox"/>'s trigger callback, so it runs inside the physics step.
        /// Implementations may close the damage window from here — calling
        /// <see cref="HitBox.Deactivate"/> during this call is safe and is how single-hit swings
        /// stop early.
        /// </remarks>
        void OnHitBoxContact(HurtBox hurtBox);
    }
}
