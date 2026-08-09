using AlpineLib.Body;
using AlpineLib.Tags;
using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// One resolved hit, ready to be applied to a <see cref="HurtBox"/>: how much damage it carries,
    /// what it is tagged as, the wound it inflicts, and who is responsible for it.
    /// </summary>
    /// <remarks>
    /// The packet is the seam between deciding a hit and applying it. Whoever owns the hit box —
    /// <see cref="CombatSystem"/>, a projectile, a hazard — rolls the outcome and fills a packet,
    /// then hands it to <see cref="DamageResolver.Apply"/>, which knows nothing about where the hit
    /// came from. Being a readonly struct it copies freely and is passed by <c>in</c> reference, so
    /// resolution never allocates.
    /// </remarks>
    public readonly struct DamagePacket {
        /// <summary>
        /// Damage carried by this hit, in health units, before the struck part's severity multiplier.
        /// </summary>
        public float Amount { get; }

        /// <summary>
        /// What this hit is, for tag-conditional stat queries — damage type, weapon class, element.
        /// May be null or empty, which reads as an untagged hit.
        /// </summary>
        public TagSet Tags { get; }

        /// <summary>
        /// Wound this hit inflicts. A packet without one lands no injury at all — see
        /// <see cref="DamageResolver.Apply"/>.
        /// </summary>
        public InjuryDefinition Injury { get; }

        /// <summary>
        /// How bad the inflicted wound is. Scales its bleeding and condition onset progress.
        /// </summary>
        public float InjurySeverity { get; }

        /// <summary>
        /// Game object credited with the hit, reported on to <see cref="IHitReceiver.NotifyHit"/> so
        /// the victim can react to its attacker. May be null for world damage with no author.
        /// </summary>
        public GameObject Instigator { get; }

        /// <summary>
        /// Builds a packet from an already-resolved outcome.
        /// </summary>
        /// <param name="amount">Damage in health units, before the part's severity multiplier.</param>
        /// <param name="tags">Tags describing the hit, or null when it is untagged.</param>
        /// <param name="injury">Wound to inflict, or null to land no injury and no damage.</param>
        /// <param name="injurySeverity">Severity of the inflicted wound.</param>
        /// <param name="instigator">Game object credited with the hit, or null for world damage.</param>
        public DamagePacket(float amount, TagSet tags, InjuryDefinition injury, float injurySeverity, GameObject instigator) {
            Amount = amount;
            Tags = tags;
            Injury = injury;
            InjurySeverity = injurySeverity;
            Instigator = instigator;
        }
    }
}
