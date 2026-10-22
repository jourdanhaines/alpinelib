using System;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Maps {
    /// <summary>Bake priority: live cells before off-screen ones, then nearest the focus (Chebyshev ring, then squared distance), then row-major.</summary>
    public static class MapBakeOrder {
        /// <summary>Negative when <paramref name="first"/> bakes before <paramref name="second"/>.</summary>
        public static int Compare(CellCoord first, MapBakeCost firstCost, CellCoord second, MapBakeCost secondCost, CellCoord focus) {
            int byCost = Rank(firstCost).CompareTo(Rank(secondCost));
            if (byCost != 0) return byCost;

            int byRing = Chebyshev(first, focus).CompareTo(Chebyshev(second, focus));
            if (byRing != 0) return byRing;

            int byDistance = SquaredDistance(first, focus).CompareTo(SquaredDistance(second, focus));
            if (byDistance != 0) return byDistance;

            return CellRunCodec.CompareRowMajor(first, second);
        }

        /// <summary>Chebyshev (ring) distance between two cells.</summary>
        public static long Chebyshev(CellCoord first, CellCoord second) {
            return Math.Max(Math.Abs((long)first.X - second.X), Math.Abs((long)first.Y - second.Y));
        }

        /// <summary>Squared Euclidean distance between two cells.</summary>
        public static long SquaredDistance(CellCoord first, CellCoord second) {
            long deltaX = (long)first.X - second.X;
            long deltaY = (long)first.Y - second.Y;
            return deltaX * deltaX + deltaY * deltaY;
        }

        // Unavailable sorts last.
        private static int Rank(MapBakeCost cost) {
            return cost == MapBakeCost.Unavailable ? int.MaxValue : (int)cost;
        }
    }
}
