using System.Collections.Generic;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>Total order of sites: tier, then id, then block, so sorting never depends on query order.</summary>
    internal sealed class LandmarkSiteOrder : IComparer<LandmarkSite> {
        public static readonly LandmarkSiteOrder Instance = new LandmarkSiteOrder();

        public int Compare(LandmarkSite left, LandmarkSite right) {
            int byTier = left.Tier.CompareTo(right.Tier);
            if (byTier != 0) {
                return byTier;
            }
            int byId = left.Id.CompareTo(right.Id);
            if (byId != 0) {
                return byId;
            }
            int byX = left.Block.X.CompareTo(right.Block.X);
            return byX != 0 ? byX : left.Block.Y.CompareTo(right.Block.Y);
        }
    }
}
