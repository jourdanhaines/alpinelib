namespace AlpineLib.Networking {
    /// <summary>
    /// Script execution order pins for the networking components that write the same transforms every
    /// frame.
    /// </summary>
    /// <remarks>
    /// Unity's default order is arbitrary per project load, and this pipeline has a required one: the
    /// transport must be pumped and the clock advanced before anything samples an interpolator, and the
    /// player's controller must have committed this frame's intent before the sync reads it. Constants
    /// rather than magic numbers on the attributes, so the ordering contract is visible in one place.
    /// </remarks>
    public static class NetExecutionOrder {
        /// <summary>Transport pump and clock advance run before every default-order script.</summary>
        public const int NetworkService = -100;

        /// <summary>
        /// Pawn drivers run after default-order scripts — player controllers among them — so they
        /// consume this frame's intent and the freshest clock.
        /// </summary>
        public const int PawnDrivers = 50;
    }
}
