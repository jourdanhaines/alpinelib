using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>
    /// Pure marker placement: a target inside the viewport (shrunk by an inset) is drawn where it is; one
    /// outside is pulled back toward the centre along its bearing until it touches the inset edge, so it
    /// slides continuously along the edge as the target moves.
    /// </summary>
    public static class MapMarkerMath {
        /// <summary>
        /// <paramref name="point"/> clamped into the rect of <paramref name="halfExtents"/> shrunk by
        /// <paramref name="inset"/>, along the ray from the centre.
        /// </summary>
        public static Vector2 EdgeClamp(Vector2 point, Vector2 halfExtents, float inset, out bool clamped) {
            float limitX = Mathf.Max(0f, halfExtents.x - inset);
            float limitY = Mathf.Max(0f, halfExtents.y - inset);
            float absoluteX = Mathf.Abs(point.x);
            float absoluteY = Mathf.Abs(point.y);
            clamped = absoluteX > limitX || absoluteY > limitY;
            if (!clamped) return point;

            float scaleX = absoluteX > 0f ? limitX / absoluteX : float.PositiveInfinity;
            float scaleY = absoluteY > 0f ? limitY / absoluteY : float.PositiveInfinity;
            return point * Mathf.Min(scaleX, scaleY);
        }

        /// <summary><paramref name="point"/> clamped into the circle of <paramref name="radius"/> shrunk by <paramref name="inset"/>.</summary>
        public static Vector2 EdgeClampCircle(Vector2 point, float radius, float inset, out bool clamped) {
            float limit = Mathf.Max(0f, radius - inset);
            float length = point.magnitude;
            clamped = length > limit;
            if (!clamped) return point;

            return point * (limit / length);
        }

        /// <summary>
        /// Placement of a target at <paramref name="viewportPoint"/> in a viewport of
        /// <paramref name="viewportSize"/> (circle = inscribed circle), kept <paramref name="inset"/> inside the edge.
        /// </summary>
        public static MapMarkerPlacement Place(Vector2 viewportPoint, Vector2 viewportSize, float inset, bool circle) {
            bool clamped;
            Vector2 position = circle
                ? EdgeClampCircle(viewportPoint, Mathf.Min(viewportSize.x, viewportSize.y) * 0.5f, inset, out clamped)
                : EdgeClamp(viewportPoint, viewportSize * 0.5f, inset, out clamped);
            Vector2 direction = viewportPoint.sqrMagnitude > 1e-12f ? viewportPoint.normalized : Vector2.up;
            return new MapMarkerPlacement(position, clamped, direction);
        }

        /// <summary>Offset of an arrow orbiting a marker at <paramref name="radius"/>, pointing along <paramref name="direction"/>.</summary>
        public static Vector2 OrbitOffset(Vector2 direction, float radius) {
            return direction * radius;
        }

        /// <summary>Z rotation that turns a +X-pointing sprite along <paramref name="direction"/>, degrees.</summary>
        public static float AngleDegrees(Vector2 direction) {
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        /// <summary>Bearing of <paramref name="direction"/>, degrees clockwise from viewport up, in [0, 360).</summary>
        public static float BearingDegrees(Vector2 direction) {
            float bearing = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            return bearing < 0f ? bearing + 360f : bearing;
        }
    }
}
