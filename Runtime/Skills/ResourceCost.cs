using System;
using AlpineLib.Vitals;
using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// One line of a skill's price: how much of a single <see cref="ResourceDefinition"/> using the
    /// skill costs. A <see cref="SkillDefinition"/> holds an array of these, so one skill can bill
    /// mana and stamina at the same time.
    /// </summary>
    /// <remarks>
    /// The cost is authored as a raw amount and scaled at spend time by the caster's cost multiplier
    /// stat, so passives and gear that make skills cheaper never have to rewrite the asset.
    /// A cost whose <see cref="resource"/> is null, or whose caster has no pool for that resource, is
    /// treated as free rather than as unaffordable: an actor without a mana pool can still cast.
    /// </remarks>
    [Serializable]
    public struct ResourceCost {
        [Tooltip("Pool this cost is billed against")]
        public ResourceDefinition resource;

        [Tooltip("Amount deducted before the cost multiplier stat is applied")]
        public float amount;
    }
}
