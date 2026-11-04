using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Builds faceted columns by lofting a stack of polygon rings: hoodoos, mesas, cactus arms,
    /// backdrop silhouettes. Optional fan caps close the ends.
    /// </summary>
    /// <remarks>
    /// Ring corners are computed once and reused by the side and cap faces, so a capped loft is
    /// watertight. Caps fan from the ring's axis point (0, y, 0), so polygons must be star-shaped about
    /// the y axis, as every <see cref="JitterPolygon"/> output is (a vertex mean can leave the kernel of
    /// a jittered ring and fold the cap). A ring whose polygon is a single repeated point makes a cone
    /// tip but leaves zero-area faces. Winding is read from the first ring with non-zero area. Twist uses <c>Math.Cos/Sin</c>, so twisted rings may differ in the
    /// last float bit across runtimes; untwisted rings are exact. Static and thread-safe.
    /// </remarks>
    public static class LoftMesh {
        private const double TwoPi = 6.2831853071795862;
        private const ulong RadiusSalt = 0x52UL;
        private const ulong AngleSalt = 0x41UL;
        private const double MinOrientationArea = 1e-12;

        /// <summary>A new loft of <paramref name="rings"/> (bottom to top), optionally capped.</summary>
        public static FacetMesh Build(IReadOnlyList<LoftRing> rings, bool capTop, bool capBottom) {
            FacetMesh mesh = new FacetMesh(EstimateFaces(rings, capTop, capBottom));
            AppendTo(mesh, rings, capTop, capBottom);
            return mesh;
        }

        /// <summary>Appends a loft of <paramref name="rings"/> (bottom to top) to <paramref name="target"/>.</summary>
        public static void AppendTo(FacetMesh target, IReadOnlyList<LoftRing> rings, bool capTop, bool capBottom) {
            if (target == null) {
                throw new ArgumentNullException(nameof(target));
            }
            int sides = Validate(rings);
            bool reversed = IsReversed(rings);
            Vector3[] corners = RingCorners(rings, sides, reversed);
            for (int ring = 0; ring + 1 < rings.Count; ring++) {
                AddBand(target, corners, ring * sides, (ring + 1) * sides, sides);
            }
            if (capBottom) {
                AddCap(target, corners, 0, sides, false);
            }
            if (capTop) {
                AddCap(target, corners, (rings.Count - 1) * sides, sides, true);
            }
        }

        /// <summary>
        /// A ring of <paramref name="sides"/> points on the ellipse with radii (<paramref name="radiusX"/>,
        /// <paramref name="radiusZ"/>), each point's radius and angle jittered by up to
        /// <paramref name="jitter"/> (0–1, fraction of the radius and of half the angular step).
        /// </summary>
        /// <remarks>
        /// With <paramref name="inwardOnly"/> the radius only shrinks, so no point leaves the ellipse's
        /// bounding box (|x| ≤ radiusX, |z| ≤ radiusZ): use it when the polygon must stay inside a collider.
        /// Points come out in increasing-angle order and never cross, whatever the jitter, and the ring is
        /// star-shaped about the origin, so caps fan cleanly. Neighbours sit at most 1.5 angular steps apart,
        /// so use at least 4 sides at full jitter (3 sides can reach 180° and a degenerate cap face).
        /// </remarks>
        public static Vector2[] JitterPolygon(ulong seed, int sides, float radiusX, float radiusZ, float jitter, bool inwardOnly) {
            if (sides < 3) {
                throw new ArgumentOutOfRangeException(nameof(sides), sides, "Must be at least 3.");
            }
            double amount = Math.Min(1.0, Math.Max(0.0, jitter));
            ulong radiusSeed = StableHash.Mix(seed, RadiusSalt);
            ulong angleSeed = StableHash.Mix(seed, AngleSalt);
            Vector2[] polygon = new Vector2[sides];
            for (int side = 0; side < sides; side++) {
                double radiusRoll = LatticeHash.Unit(radiusSeed, side);
                double angleRoll = LatticeHash.Unit(angleSeed, side) - 0.5;
                double scale = inwardOnly ? 1.0 - amount * radiusRoll : 1.0 + amount * (2.0 * radiusRoll - 1.0);
                double angle = TwoPi * (side + angleRoll * 0.5 * amount) / sides;
                polygon[side] = new Vector2((float)(radiusX * (Math.Cos(angle) * scale)), (float)(radiusZ * (Math.Sin(angle) * scale)));
            }
            return polygon;
        }

        private static int Validate(IReadOnlyList<LoftRing> rings) {
            if (rings == null) {
                throw new ArgumentNullException(nameof(rings));
            }
            if (rings.Count < 2) {
                throw new ArgumentException("A loft needs at least two rings.", nameof(rings));
            }
            int sides = rings[0].Polygon?.Length ?? 0;
            if (sides < 3) {
                throw new ArgumentException("Ring polygons need at least three points.", nameof(rings));
            }
            for (int ring = 1; ring < rings.Count; ring++) {
                if ((rings[ring].Polygon?.Length ?? 0) != sides) {
                    throw new ArgumentException($"Ring {ring} has a different point count from ring 0.", nameof(rings));
                }
            }
            return sides;
        }

        private static int EstimateFaces(IReadOnlyList<LoftRing> rings, bool capTop, bool capBottom) {
            if (rings == null || rings.Count < 2 || rings[0].Polygon == null) {
                return 0;
            }
            int sides = rings[0].Polygon.Length;
            int caps = (capTop ? 1 : 0) + (capBottom ? 1 : 0);
            return sides * (2 * (rings.Count - 1) + caps);
        }

        private static bool IsReversed(IReadOnlyList<LoftRing> rings) {
            for (int ring = 0; ring < rings.Count; ring++) {
                double area = SignedArea(rings[ring].Polygon);
                if (Math.Abs(area) > MinOrientationArea) {
                    return area < 0.0;
                }
            }
            throw new ArgumentException("Every ring polygon has zero area, so the loft has no outward side.", nameof(rings));
        }

        // Twice the polygon's signed area in (x, z); positive for increasing-angle order.
        private static double SignedArea(Vector2[] polygon) {
            double area = 0.0;
            for (int point = 0; point < polygon.Length; point++) {
                Vector2 current = polygon[point];
                Vector2 next = polygon[(point + 1) % polygon.Length];
                area += (double)current.X * next.Y - (double)next.X * current.Y;
            }
            return area;
        }

        private static Vector3[] RingCorners(IReadOnlyList<LoftRing> rings, int sides, bool reversed) {
            Vector3[] corners = new Vector3[rings.Count * sides];
            for (int ring = 0; ring < rings.Count; ring++) {
                FillRing(corners, ring * sides, rings[ring], sides, reversed);
            }
            return corners;
        }

        private static void FillRing(Vector3[] corners, int start, LoftRing ring, int sides, bool reversed) {
            bool twisted = ring.Twist != 0f;
            double cosine = twisted ? Math.Cos(ring.Twist) : 1.0;
            double sine = twisted ? Math.Sin(ring.Twist) : 0.0;
            for (int side = 0; side < sides; side++) {
                Vector2 point = ring.Polygon[reversed ? sides - 1 - side : side];
                if (!twisted) {
                    corners[start + side] = new Vector3(point.X, ring.Y, point.Y);
                    continue;
                }
                double x = point.X * cosine - point.Y * sine;
                double z = point.X * sine + point.Y * cosine;
                corners[start + side] = new Vector3((float)x, ring.Y, (float)z);
            }
        }

        // Two outward faces per polygon edge between a lower and an upper ring.
        private static void AddBand(FacetMesh target, Vector3[] corners, int lower, int upper, int sides) {
            for (int side = 0; side < sides; side++) {
                int next = (side + 1) % sides;
                Vector3 lowerHere = corners[lower + side];
                Vector3 lowerNext = corners[lower + next];
                Vector3 upperHere = corners[upper + side];
                Vector3 upperNext = corners[upper + next];
                target.AddFace(lowerHere, upperHere, lowerNext);
                target.AddFace(lowerNext, upperHere, upperNext);
            }
        }

        private static void AddCap(FacetMesh target, Vector3[] corners, int start, int sides, bool facesUp) {
            Vector3 centre = new Vector3(0f, corners[start].Y, 0f);
            for (int side = 0; side < sides; side++) {
                Vector3 here = corners[start + side];
                Vector3 next = corners[start + (side + 1) % sides];
                if (facesUp) {
                    target.AddFace(centre, next, here);
                } else {
                    target.AddFace(centre, here, next);
                }
            }
        }
    }
}
