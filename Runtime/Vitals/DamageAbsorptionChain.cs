using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Vitals {
    /// <summary>
    /// Ordered stack of <see cref="ResourcePool"/>s that damage falls through. Each pool absorbs as
    /// much as it currently holds and the overflow cascades to the next, so a shield listed ahead of
    /// health soaks the hit until it empties.
    /// </summary>
    /// <remarks>
    /// The chain only drains pools; it never decides what an unabsorbed remainder means. Callers
    /// receive that remainder from <see cref="ApplyDamage"/> and can ignore it, log overkill, or
    /// route it somewhere else.
    /// </remarks>
    public class DamageAbsorptionChain : MonoBehaviour {
        [Header("Absorption Order")]
        [Tooltip("Pools damage falls through, first to last")]
        [SerializeField] private List<ResourcePool> pools = new();

        /// <summary>Pools damage falls through, first to last.</summary>
        public IReadOnlyList<ResourcePool> Pools => pools;

        /// <summary>
        /// Raised after damage has been applied, with the amount the chain actually absorbed.
        /// Not raised when nothing was absorbed.
        /// </summary>
        public event Action<float> OnDamageAbsorbed;

        /// <summary>
        /// Drains the pools in order until the damage is spent.
        /// </summary>
        /// <returns>The part of the damage no pool could absorb, zero when the chain soaked it all.</returns>
        public float ApplyDamage(float amount) {
            if (amount <= 0f) return 0f;

            float remaining = amount;

            foreach (var pool in pools) {
                if (remaining <= 0f) break;
                if (pool == null) continue;

                remaining -= AbsorbInto(pool, remaining);
            }

            float absorbed = amount - remaining;
            if (absorbed > 0f) OnDamageAbsorbed?.Invoke(absorbed);

            return remaining;
        }

        private static float AbsorbInto(ResourcePool pool, float amount) {
            float absorbed = Mathf.Min(pool.CurrentValue, amount);
            if (absorbed <= 0f) return 0f;

            pool.Drain(absorbed);
            return absorbed;
        }
    }
}
