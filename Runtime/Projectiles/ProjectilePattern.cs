using UnityEngine;

namespace AlpineLib.Projectiles {
    /// <summary>
    /// How a multi-projectile volley arranges its shots around the caster's aim direction.
    /// </summary>
    public enum ProjectilePattern {
        /// <summary>One shot straight down the aim direction, ignoring spread.</summary>
        Single,

        /// <summary>All shots fired at once, fanned evenly across the spread arc.</summary>
        Fan,

        /// <summary>The same arc as <see cref="Fan"/>, but fired one at a time to sweep across it.</summary>
        Spiral
    }

    /// <summary>
    /// Turns a <see cref="ProjectilePattern"/> and a shot index into the direction that shot should
    /// travel.
    /// </summary>
    /// <remarks>
    /// Pattern maths is kept out of <see cref="Projectile"/> on purpose: a projectile only ever knows
    /// its own single direction, and whatever fires the volley — a skill, a weapon, a boss phase —
    /// asks this class once per shot before calling <see cref="Projectile.Launch"/>.
    /// </remarks>
    public static class ProjectilePatterns {
        /// <summary>
        /// Returns the normalized travel direction for one shot in a volley.
        /// </summary>
        /// <param name="pattern">Arrangement the volley uses.</param>
        /// <param name="index">Zero-based index of this shot within the volley.</param>
        /// <param name="count">Total shots in the volley.</param>
        /// <param name="forward">Aim direction the pattern is built around.</param>
        /// <param name="up">Axis the spread yaws about, normally the caster's up vector.</param>
        /// <param name="spreadAngle">Total width of the arc in degrees, split evenly either side of <paramref name="forward"/>.</param>
        /// <remarks>
        /// <para>
        /// <see cref="ProjectilePattern.Single"/> always returns <paramref name="forward"/>, and so
        /// does any pattern with a <paramref name="count"/> of one or less — a lone shot has no arc to
        /// sit on, so it goes straight ahead rather than to one edge of the spread.
        /// </para>
        /// <para>
        /// <see cref="ProjectilePattern.Spiral"/> returns exactly the same directions as
        /// <see cref="ProjectilePattern.Fan"/>. The sweeping look comes entirely from the caller
        /// staggering the launch times of successive indices; fire them on the same frame and a spiral
        /// is indistinguishable from a fan.
        /// </para>
        /// <para>
        /// A degenerate <paramref name="forward"/> or <paramref name="up"/> falls back to
        /// <see cref="Vector3.forward"/> and <see cref="Vector3.up"/> so a mis-configured caster still
        /// produces a usable direction instead of a zero vector.
        /// </para>
        /// </remarks>
        public static Vector3 GetDirection(ProjectilePattern pattern, int index, int count, Vector3 forward, Vector3 up, float spreadAngle) {
            Vector3 aim = forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
            if (pattern == ProjectilePattern.Single) return aim;
            if (count <= 1) return aim;

            Vector3 yawAxis = up.sqrMagnitude > 0f ? up.normalized : Vector3.up;
            float step = spreadAngle / (count - 1);
            float yaw = -spreadAngle * 0.5f + step * index;

            return Quaternion.AngleAxis(yaw, yawAxis) * aim;
        }
    }
}
