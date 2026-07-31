using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// Timing and payload data for a single melee attack: which animator trigger fires it, when
    /// during that animation the hit box is live, how long before it can be used again, and what
    /// injuries it can inflict.
    /// </summary>
    /// <remarks>
    /// The damage window is expressed in normalized animation time, so retiming the clip keeps the
    /// window aligned with the swing. The animator needs a state tagged "Attack" that the trigger
    /// reaches, otherwise <see cref="CombatSystem"/> never sees the attack start.
    /// </remarks>
    [CreateAssetMenu(fileName = "AttackDefinition", menuName = "AlpineLib/Combat/Attack Definition")]
    public class AttackDefinition : ScriptableObject {
        [Tooltip("Animator trigger to fire")]
        public string animationTrigger = "Attack";

        [Tooltip("Normalized time when hitbox activates")]
        [Range(0f, 1f)] public float damageWindowStart = 0.3f;

        [Tooltip("Normalized time when hitbox deactivates")]
        [Range(0f, 1f)] public float damageWindowEnd = 0.6f;

        [Tooltip("Seconds between uses")]
        public float cooldown;

        [Tooltip("Noise radius emitted when swinging")]
        public float attackNoiseRadius = 5f;

        [Tooltip("Maximum degrees the attacker can rotate during this attack")]
        public float maxRotation = 90f;

        [Tooltip("Possible injury results, selected by weight")]
        public AttackOutcome[] outcomes;
    }
}
