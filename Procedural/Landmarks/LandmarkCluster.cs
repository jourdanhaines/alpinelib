using System.Collections.Generic;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>One block of a cluster tier: where the cluster stands, its kind and its member sites in slot order.</summary>
    public sealed class LandmarkCluster {
        /// <summary>Stores a cluster as given.</summary>
        public LandmarkCluster(int tier, CellCoord block, CellCoord anchor, double offsetX, double offsetZ, LandmarkClusterKind kind, ulong id, bool isAttracted,
                               IReadOnlyList<LandmarkSite> members) {
            Tier = tier;
            Block = block;
            Anchor = anchor;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
            Kind = kind;
            Id = id;
            IsAttracted = isAttracted;
            Members = members;
        }

        /// <summary>Index of the tier in the field's tier list.</summary>
        public int Tier { get; }

        /// <summary>The block, in block units.</summary>
        public CellCoord Block { get; }

        /// <summary>The cell the cluster footprint is centred on.</summary>
        public CellCoord Anchor { get; }

        /// <summary>Cluster centre offset East from the anchor cell centre, metres, rounded to 1e-4.</summary>
        public double OffsetX { get; }

        /// <summary>Cluster centre offset North from the anchor cell centre, metres, rounded to 1e-4.</summary>
        public double OffsetZ { get; }

        /// <summary>The kind after any degrade.</summary>
        public LandmarkClusterKind Kind { get; }

        /// <summary>Stable identity: the tier stream mixed with the packed block.</summary>
        public ulong Id { get; }

        /// <summary>True when the field bias moved the anchor off the drawn one.</summary>
        public bool IsAttracted { get; }

        /// <summary>The member sites in slot order.</summary>
        public IReadOnlyList<LandmarkSite> Members { get; }
    }
}
