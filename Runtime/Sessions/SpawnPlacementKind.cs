namespace AlpineLib.Sessions {
    /// <summary>
    /// Which rule a session places its arrivals by.
    /// </summary>
    /// <remarks>
    /// An authoring choice rather than a runtime one: the two kinds map onto the two placements the
    /// netcode ships, and the value is exported to the dedicated server as its numeric form so both ends
    /// seat players the same way. New kinds are appended, never reordered, because the number is what
    /// travels.
    /// </remarks>
    public enum SpawnPlacementKind {
        /// <summary>Seats arrivals evenly around a ring at the origin. Needs nothing from the scene.</summary>
        Ring = 0,

        /// <summary>Hands out authored points in turn, ringing the overflow around each point.</summary>
        List = 1
    }
}
