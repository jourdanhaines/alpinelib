namespace AlpineLib.Maps {
    /// <summary>Where a bake job stands after a step.</summary>
    public enum MapBakeJobStatus {
        /// <summary>Still preparing; step again later.</summary>
        Working = 0,

        /// <summary>Ready to capture <see cref="IMapBakeJob.CaptureArea"/>.</summary>
        Ready = 1,

        /// <summary>Gave up (the cell went away); the cell is retried later.</summary>
        Failed = 2,
    }
}
