using AlpineLib.Projectiles;
using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// A skill delivered by spawning one or more <see cref="Projectile"/> instances part-way through
    /// its animation. The arrangement of a multi-shot volley comes from
    /// <see cref="ProjectilePatterns"/>; this subclass only says how many, how wide, and when.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ProjectilePattern.Single"/> and <see cref="ProjectilePattern.Fan"/> release every
    /// shot on the frame <see cref="spawnTimeNormalized"/> is reached.
    /// <see cref="ProjectilePattern.Spiral"/> uses the same directions but staggers the releases
    /// across the remainder of the animation, so the volley sweeps rather than appearing at once.
    /// </para>
    /// <para>
    /// A skill with no <see cref="projectilePrefab"/> plays its animation and fires nothing; it still
    /// costs resources and still goes on cooldown, so an unassigned prefab is a silent misfire rather
    /// than an error.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "ProjectileSkillDefinition", menuName = "AlpineLib/Skills/Projectile Skill")]
    public class ProjectileSkillDefinition : SkillDefinition {
        /// <summary>Projectile spawned for each shot in the volley.</summary>
        [Tooltip("Projectile prefab spawned for each shot")]
        public Projectile projectilePrefab;

        /// <summary>
        /// Normalized animation time at which the volley starts. For a spiral this is when the first
        /// shot leaves, not the whole volley.
        /// </summary>
        [Tooltip("Normalized time when the first shot is released")]
        [Range(0f, 1f)] public float spawnTimeNormalized = 0.5f;

        /// <summary>Number of shots in the volley. Values below one are treated as one.</summary>
        [Tooltip("Number of shots in the volley")]
        public int projectileCount = 1;

        /// <summary>
        /// Total width of the volley's arc in degrees, split evenly either side of the aim direction.
        /// Ignored by <see cref="ProjectilePattern.Single"/> and by single-shot volleys.
        /// </summary>
        [Tooltip("Total arc width in degrees, split either side of the aim direction")]
        public float spreadAngle;

        /// <summary>How the shots are arranged around the aim direction, and whether they stagger.</summary>
        [Tooltip("How the volley is arranged around the aim direction")]
        public ProjectilePattern pattern;

        /// <summary>Travel speed handed to each spawned projectile, in units per second.</summary>
        [Tooltip("Projectile travel speed in units per second")]
        public float projectileSpeed = 20f;

        /// <summary>
        /// Seconds each spawned projectile survives before destroying itself. Projectiles do not stop
        /// on level geometry, so this is what reclaims strays — keep it short.
        /// </summary>
        [Tooltip("Seconds before an unspent projectile destroys itself")]
        public float projectileLifetime = 5f;
    }
}
