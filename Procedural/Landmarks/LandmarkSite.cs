using System;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>One landmark of a <see cref="LandmarkField"/> tier: where it stands and the seeds it is shaped from.</summary>
    public readonly struct LandmarkSite : IEquatable<LandmarkSite> {
        /// <summary>Stores a site as given.</summary>
        public LandmarkSite(int tier, CellCoord block, CellCoord anchor, double offsetX, double offsetZ,
                            ulong shapeSeed, LandmarkDraws uDraws, ulong id, bool isAttracted = false) {
            Tier = tier;
            Block = block;
            Anchor = anchor;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
            ShapeSeed = shapeSeed;
            UDraws = uDraws;
            Id = id;
            IsAttracted = isAttracted;
        }

        /// <summary>True when the field bias moved the anchor off the drawn one.</summary>
        public bool IsAttracted { get; }

        /// <summary>Index of the tier in the field's tier list.</summary>
        public int Tier { get; }

        /// <summary>The block holding the site, in block units.</summary>
        public CellCoord Block { get; }

        /// <summary>The cell the footprint is centred on.</summary>
        public CellCoord Anchor { get; }

        /// <summary>Offset East from the anchor cell centre, metres, rounded to 1e-4.</summary>
        public double OffsetX { get; }

        /// <summary>Offset North from the anchor cell centre, metres, rounded to 1e-4.</summary>
        public double OffsetZ { get; }

        /// <summary>Seed for the landmark's shape.</summary>
        public ulong ShapeSeed { get; }

        /// <summary>Spare uniform draws.</summary>
        public LandmarkDraws UDraws { get; }

        /// <summary>Stable identity: the tier stream mixed with the packed block.</summary>
        public ulong Id { get; }

        /// <inheritdoc />
        public bool Equals(LandmarkSite other) {
            return Tier == other.Tier && Block == other.Block && Anchor == other.Anchor
                && OffsetX.Equals(other.OffsetX) && OffsetZ.Equals(other.OffsetZ)
                && ShapeSeed == other.ShapeSeed && UDraws.Equals(other.UDraws) && Id == other.Id && IsAttracted == other.IsAttracted;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is LandmarkSite other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                return (Tier * 397) ^ Id.GetHashCode();
            }
        }
    }
}
