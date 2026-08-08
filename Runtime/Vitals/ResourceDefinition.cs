using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Vitals {
    /// <summary>
    /// Asset that describes one consumable pool — health, mana, stamina, a shield. Games author one
    /// asset per resource and reference it from a <see cref="ResourcePool"/>, so the resource
    /// vocabulary lives in game data rather than in library code.
    /// </summary>
    /// <remarks>
    /// Capacity and regeneration can either be authored as flat numbers or bound to a
    /// <see cref="StatDefinition"/>. When a stat is assigned the pool reads it live from the
    /// <see cref="StatSheet"/> beside it and the matching base value becomes a fallback, used only
    /// when the owner has no stat sheet.
    /// </remarks>
    [CreateAssetMenu(fileName = "ResourceDefinition", menuName = "AlpineLib/Vitals/Resource Definition")]
    public class ResourceDefinition : ScriptableObject {
        [Header("Identity")]
        [Tooltip("Human readable name for user interfaces and tooling")]
        public string displayName;

        [Tooltip("Tint a bar or gauge should use when presenting this resource")]
        public Color barColor = Color.white;

        [Header("Capacity")]
        [Tooltip("Ceiling used when no max value stat is bound")]
        public float baseMaxValue = 100f;

        [Tooltip("Optional stat that supplies the ceiling, read live from the owner's stat sheet")]
        public StatDefinition maxValueStat;

        [Tooltip("Whether the pool starts at its ceiling rather than empty")]
        public bool startsFull = true;

        [Header("Regeneration")]
        [Tooltip("Refill rate in units per second used when no regeneration stat is bound. Zero disables regeneration")]
        public float baseRegenPerSecond;

        [Tooltip("Optional stat that supplies the refill rate, read live from the owner's stat sheet")]
        public StatDefinition regenPerSecondStat;

        [Tooltip("Seconds of quiet required after the pool is drained before regeneration resumes")]
        public float regenDelaySeconds;
    }
}
