using System;
using AlpineLib.Body;
using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// One possible result of a landed attack. An <see cref="AttackDefinition"/> holds several and
    /// picks between them by weight, so the same swing can graze or maim.
    /// </summary>
    [Serializable]
    public struct AttackOutcome {
        [Tooltip("Relative probability for random selection")]
        public float weight;

        [Tooltip("What injury type this produces")]
        public InjuryDefinition injuryDefinition;

        [Tooltip("How severe this instance is")]
        public float severity;

        [Tooltip("Base hit damage dealt to health")]
        public float baseDamage;
    }
}
