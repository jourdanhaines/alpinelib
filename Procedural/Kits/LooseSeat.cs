using System;

namespace AlpineLib.Procedural.Kits {
    /// <summary>
    /// Seats a loose turned rock on a walkable surface. The rock is a box turned by whole degrees; it is lowered until a grid
    /// over its turned bottom face lies under the surface and the low points of the ellipsoid in its box stand at most the
    /// slack over it, so no side lifts off a crease. Burial is measured on that ellipsoid, closer to a rock mesh than the box.
    /// </summary>
    public static class LooseSeat {
        /// <summary>
        /// The box centre height at most <paramref name="centreY"/> at which the box (half extents before turning) centred over
        /// (x, z) has its bottom grid under <paramref name="surfaceAt"/> and its ellipsoid low points within the slack over it.
        /// </summary>
        public static double Seated(in DegreeRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, Func<double, double, double> surfaceAt,
                                    in LooseSeatSettings settings) {
            if (surfaceAt == null) throw new ArgumentNullException(nameof(surfaceAt));
            if (!settings.IsBuilt) throw new ArgumentException("Settings were never constructed.", nameof(settings));

            int grid = settings.BottomGrid;
            double over = double.NegativeInfinity;
            for (int row = 0; row < grid; row++) {
                for (int column = 0; column < grid; column++) {
                    double shareX = row * 2.0 / (grid - 1) - 1.0, shareZ = column * 2.0 / (grid - 1) - 1.0;
                    over = Math.Max(over, Over(rotation, x, centreY, z, shareX * halfX, -halfY, shareZ * halfZ, surfaceAt));
                }
            }
            int lattice = settings.LatticeCount;
            for (int row = 0; row < lattice; row++) {
                for (int column = 0; column < lattice; column++) {
                    double shareX = settings.LatticeAt(row), shareZ = settings.LatticeAt(column);
                    double lowY = -halfY * Math.Sqrt(1.0 - shareX * shareX - shareZ * shareZ);
                    over = Math.Max(over, Over(rotation, x, centreY, z, shareX * halfX, lowY, shareZ * halfZ, surfaceAt) - settings.LowPointSlack);
                }
            }
            return centreY - Math.Max(0.0, over);
        }

        /// <summary>The share of the rock's rounded height (its box's ellipsoid, turned) under <paramref name="surfaceAt"/> at its centre.</summary>
        public static double Buried(in DegreeRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, Func<double, double, double> surfaceAt) {
            if (surfaceAt == null) throw new ArgumentNullException(nameof(surfaceAt));

            double rounded = rotation.RoundedExtentY(halfX, halfY, halfZ);
            return (surfaceAt(x, z) - (centreY - rounded)) / (2.0 * rounded);
        }

        // How far the local box point (turned about the centre) stands over the surface under it.
        private static double Over(in DegreeRotation rotation, double x, double y, double z, double localX, double localY, double localZ, Func<double, double, double> surfaceAt) {
            rotation.Apply(localX, localY, localZ, out double turnedX, out double turnedY, out double turnedZ);
            return y + turnedY - surfaceAt(x + turnedX, z + turnedZ);
        }
    }
}
