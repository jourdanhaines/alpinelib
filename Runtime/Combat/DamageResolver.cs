using AlpineLib.Body;

namespace AlpineLib.Combat {
    /// <summary>
    /// Applies a resolved <see cref="DamagePacket"/> to the body behind a <see cref="HurtBox"/>:
    /// lands the injury, reports the damage, flashes the hurt box, and notifies any
    /// <see cref="IHitReceiver"/> above it.
    /// </summary>
    /// <remarks>
    /// This is the application half of a hit, split out so every damage source shares one code path.
    /// It deliberately makes no decisions: no self-hit filtering, no outcome rolling, no damage-window
    /// bookkeeping and no events. Those belong to the <see cref="IHitBoxOwner"/> that produced the
    /// packet, which must filter friendly fire itself before calling in.
    /// </remarks>
    public static class DamageResolver {
        /// <summary>
        /// Applies one hit to the body behind a hurt box.
        /// </summary>
        /// <param name="packet">The resolved hit to apply.</param>
        /// <param name="hurtBox">Damageable region that was struck. Ignored when null.</param>
        /// <remarks>
        /// A packet with no <see cref="DamagePacket.Injury"/> is dropped whole — no injury, no damage
        /// tick, no flash, no hit notification. That mirrors <see cref="CombatSystem.OnHitBoxContact"/>,
        /// where an outcome without an injury definition is a whiff rather than a bare damage hit, so
        /// pure-damage packets are not currently expressible. Damage only reaches the victim through
        /// <see cref="BodySystem.ApplyInjury"/>; a hurt box with no <see cref="BodySystem"/> above it
        /// still flashes and still notifies, it just takes nothing.
        /// </remarks>
        public static void Apply(in DamagePacket packet, HurtBox hurtBox) {
            if (hurtBox == null) return;
            if (packet.Injury == null) return;

            var injury = new Injury(packet.Injury, packet.InjurySeverity);
            hurtBox.GetComponentInParent<BodySystem>()?.ApplyInjury(hurtBox.BodyPart, injury, packet.Amount);

            hurtBox.Flash();
            hurtBox.GetComponentInParent<IHitReceiver>()?.NotifyHit(packet.Instigator, hurtBox.BodyPart);
        }
    }
}
