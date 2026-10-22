namespace AlpineLib.Maps {
    /// <summary>What baking a cell would take from a source. Ordered: cheaper first.</summary>
    public enum MapBakeCost {
        /// <summary>The source cannot bake this cell now.</summary>
        Unavailable = 0,

        /// <summary>The cell is already built in the world; only a capture is needed.</summary>
        Live = 1,

        /// <summary>The cell must be built off-screen first.</summary>
        Offscreen = 2,
    }
}
