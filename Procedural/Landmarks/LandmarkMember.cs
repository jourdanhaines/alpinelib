using System;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>
    /// Where a site sits in its cluster: the cluster id and kind, its slot (0 = the large or lonely landmark, 1 = the
    /// second large of a multi, 2+ = small ones), how many members the cluster has, its size draw and its ring placement
    /// (a bearing in whole degrees from the cluster centre, 0 = +X, 90 = +Z, and a uniform ring draw the consumer maps to a
    /// distance). A classic (non-cluster) site has <see cref="Count"/> 0.
    /// </summary>
    public readonly struct LandmarkMember : IEquatable<LandmarkMember> {
        /// <summary>Stores a member as given.</summary>
        public LandmarkMember(ulong clusterId, LandmarkClusterKind kind, int slot, int count, bool isLarge, double across, int bearingDegrees, double ring) {
            ClusterId = clusterId;
            Kind = kind;
            Slot = slot;
            Count = count;
            IsLarge = isLarge;
            Across = across;
            BearingDegrees = bearingDegrees;
            Ring = ring;
        }

        /// <summary>The cluster's id (the block stream); every member shares it.</summary>
        public ulong ClusterId { get; }

        /// <summary>The cluster kind after any degrade.</summary>
        public LandmarkClusterKind Kind { get; }

        /// <summary>Fixed draw slot: 0 large/lonely, 1 second large, 2 and up small.</summary>
        public int Slot { get; }

        /// <summary>Members in the cluster; 0 for a classic site.</summary>
        public int Count { get; }

        /// <summary>True for a cluster's large landmark (slots 0 and 1 of a group or multi).</summary>
        public bool IsLarge { get; }

        /// <summary>Drawn size across (longest plan extent), metres, rounded to 1e-4.</summary>
        public double Across { get; }

        /// <summary>Bearing from the cluster centre, whole degrees in [0, 360).</summary>
        public int BearingDegrees { get; }

        /// <summary>Uniform ring draw in [0, 1), floored to 1e-6.</summary>
        public double Ring { get; }

        /// <summary>True when the site belongs to a cluster tier.</summary>
        public bool IsClustered => Count > 0;

        /// <inheritdoc />
        public bool Equals(LandmarkMember other) {
            return ClusterId == other.ClusterId && Kind == other.Kind && Slot == other.Slot && Count == other.Count && IsLarge == other.IsLarge
                && Across.Equals(other.Across) && BearingDegrees == other.BearingDegrees && Ring.Equals(other.Ring);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is LandmarkMember other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                return (ClusterId.GetHashCode() * 397) ^ Slot;
            }
        }
    }
}
