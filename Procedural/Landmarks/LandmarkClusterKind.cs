namespace AlpineLib.Procedural.Landmarks {
    /// <summary>How a cluster tier block groups its landmarks.</summary>
    public enum LandmarkClusterKind {
        /// <summary>One landmark on its own.</summary>
        Lonely = 0,

        /// <summary>One large landmark with small ones round it.</summary>
        Group = 1,

        /// <summary>Two large landmarks with small ones round them.</summary>
        Multi = 2,
    }
}
