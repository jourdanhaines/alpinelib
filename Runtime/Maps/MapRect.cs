using System;
using System.Globalization;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Maps {
    /// <summary>An axis-aligned map rectangle in absolute metres (X east, Z north).</summary>
    public readonly struct MapRect : IEquatable<MapRect> {
        /// <summary>Builds a rect from its corners; min and max are sorted.</summary>
        public MapRect(double minX, double minZ, double maxX, double maxZ) {
            MinX = Math.Min(minX, maxX);
            MinZ = Math.Min(minZ, maxZ);
            MaxX = Math.Max(minX, maxX);
            MaxZ = Math.Max(minZ, maxZ);
        }

        /// <summary>West edge.</summary>
        public double MinX { get; }

        /// <summary>South edge.</summary>
        public double MinZ { get; }

        /// <summary>East edge.</summary>
        public double MaxX { get; }

        /// <summary>North edge.</summary>
        public double MaxZ { get; }

        /// <summary>East-west extent.</summary>
        public double Width => MaxX - MinX;

        /// <summary>North-south extent.</summary>
        public double Height => MaxZ - MinZ;

        /// <summary>Centre point.</summary>
        public MapPoint Centre => new MapPoint((MinX + MaxX) * 0.5, (MinZ + MaxZ) * 0.5);

        /// <summary>The metres covered by <paramref name="cells"/> on a grid of <paramref name="cellSize"/> (cell (0,0) spans [0, size)).</summary>
        public static MapRect FromCells(GridRect cells, double cellSize) {
            return new MapRect(cells.XMin * cellSize, cells.YMin * cellSize, cells.XMaxExclusive * cellSize, cells.YMaxExclusive * cellSize);
        }

        /// <summary>True when <paramref name="point"/> lies inside or on the edge.</summary>
        public bool Contains(MapPoint point) {
            return point.X >= MinX && point.X <= MaxX && point.Z >= MinZ && point.Z <= MaxZ;
        }

        /// <summary>The nearest point inside the rect.</summary>
        public MapPoint Clamp(MapPoint point) {
            return new MapPoint(Math.Min(Math.Max(point.X, MinX), MaxX), Math.Min(Math.Max(point.Z, MinZ), MaxZ));
        }

        /// <summary>Grown by <paramref name="metres"/> on every side.</summary>
        public MapRect Inflate(double metres) {
            return new MapRect(MinX - metres, MinZ - metres, MaxX + metres, MaxZ + metres);
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(MapRect left, MapRect right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(MapRect left, MapRect right) {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(MapRect other) {
            return MinX.Equals(other.MinX) && MinZ.Equals(other.MinZ) && MaxX.Equals(other.MaxX) && MaxZ.Equals(other.MaxZ);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is MapRect other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                int hash = MinX.GetHashCode();
                hash = (hash * 397) ^ MinZ.GetHashCode();
                hash = (hash * 397) ^ MaxX.GetHashCode();
                return (hash * 397) ^ MaxZ.GetHashCode();
            }
        }

        /// <summary>"[minX,minZ..maxX,maxZ]", culture-invariant.</summary>
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "[{0:0.###},{1:0.###}..{2:0.###},{3:0.###}]", MinX, MinZ, MaxX, MaxZ);
        }
    }
}
