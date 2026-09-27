namespace AlpineLib.Procedural.Streaming {
    /// <summary>Where a streamed cell is in its build.</summary>
    public enum StreamedCellState {
        /// <summary>Wanted, waiting for a worker.</summary>
        Queued,

        /// <summary><see cref="ICellBuilder.Prepare"/> is running on a worker.</summary>
        Preparing,

        /// <summary>Prepared, waiting for main-thread time.</summary>
        Prepared,

        /// <summary><see cref="ICellBuilder.Instantiate"/> is running in budgeted slices.</summary>
        Instantiating,

        /// <summary>Fully built.</summary>
        Live,

        /// <summary>Prepare or Instantiate threw; stays empty until the cell leaves interest.</summary>
        Failed,

        /// <summary>Dropped from the streamer.</summary>
        Released
    }
}
