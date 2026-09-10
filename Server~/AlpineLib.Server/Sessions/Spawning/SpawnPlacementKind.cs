namespace AlpineLib.Server.Sessions.Spawning {
    /// <summary>
    /// Which rule a session places its arrivals by.
    /// </summary>
    /// <remarks>
    /// The numbers mirror the Unity-side <c>AlpineLib.Sessions.SpawnPlacementKind</c>, because the
    /// exporter writes the kind as its numeric form and this is what reads it back. New kinds are
    /// appended, never reordered.
    /// </remarks>
    public enum SpawnPlacementKind {
        /// <summary>Seats arrivals evenly around a ring at the origin. Needs nothing from the scene.</summary>
        Ring = 0,

        /// <summary>Hands out authored points in turn, ringing the overflow around each point.</summary>
        List = 1
    }
}
