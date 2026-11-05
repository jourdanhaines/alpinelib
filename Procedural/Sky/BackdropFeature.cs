using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One silhouette of the backdrop ring: its sector, where it stands in that sector (absolute-world metres
    /// from the sector's min corner), its kind and mesh variant, yaw, and size.
    /// </summary>
    public readonly struct BackdropFeature {
        public BackdropFeature(int sectorX, int sectorZ, double localX, double localZ, BackdropFeatureKind kind, int variant,
            double yaw, double width, double height, double depth, double altitude) {
            SectorX = sectorX;
            SectorZ = sectorZ;
            LocalX = localX;
            LocalZ = localZ;
            Kind = kind;
            Variant = variant;
            Yaw = yaw;
            Width = width;
            Height = height;
            Depth = depth;
            Altitude = altitude;
        }

        public int SectorX { get; }
        public int SectorZ { get; }

        /// <summary>Metres from the sector's West edge, in [0, sector size).</summary>
        public double LocalX { get; }

        /// <summary>Metres from the sector's South edge, in [0, sector size).</summary>
        public double LocalZ { get; }

        public BackdropFeatureKind Kind { get; }

        /// <summary>Mesh variant of the kind, in [0, <see cref="BackdropSettings.VariantsPerKind"/>).</summary>
        public int Variant { get; }

        /// <summary>Rotation about +y, radians.</summary>
        public double Yaw { get; }

        /// <summary>Footprint width (the mesh's unit x), metres.</summary>
        public double Width { get; }

        /// <summary>Height of the silhouette, metres.</summary>
        public double Height { get; }

        /// <summary>Scale of the mesh's unit z, metres: the width for rock, about the height for a cloud bank (see <see cref="BackdropSilhouette"/>).</summary>
        public double Depth { get; }

        /// <summary>Height of its base above the ground, metres (0 for rock).</summary>
        public double Altitude { get; }

        /// <summary>Absolute-world x of the feature with sectors of <paramref name="sectorSize"/> metres.</summary>
        public double AbsoluteX(double sectorSize) {
            return SectorX * sectorSize + LocalX;
        }

        /// <summary>Absolute-world z of the feature with sectors of <paramref name="sectorSize"/> metres.</summary>
        public double AbsoluteZ(double sectorSize) {
            return SectorZ * sectorSize + LocalZ;
        }

        /// <summary>Index of the feature's mesh among every kind's variants.</summary>
        public int MeshIndex => (int)Kind * BackdropSettings.VariantsPerKind + Variant;

        /// <summary>Farthest any part of the feature's footprint lies from its centre, at any yaw, metres.</summary>
        public double Reach => ReachOf(Kind, Width, Depth);

        /// <summary>
        /// Footprint reach of a <paramref name="kind"/> silhouette <paramref name="width"/> by <paramref name="depth"/>:
        /// rock's unit footprint fits a half-unit square, a cloud bank's z runs up to one depth either side.
        /// </summary>
        public static double ReachOf(BackdropFeatureKind kind, double width, double depth) {
            double halfDepth = kind == BackdropFeatureKind.CloudBank ? depth : depth * 0.5;
            return Math.Sqrt(width * width * 0.25 + halfDepth * halfDepth);
        }
    }
}
