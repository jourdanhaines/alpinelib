namespace AlpineLib.Sky {
    /// <summary>Seconds since the session began, on a clock every client agrees on (as far as it can).</summary>
    public interface ISessionClock {
        /// <summary>Session time in seconds; never decreases on purpose, may snap when a shared clock resyncs.</summary>
        double Seconds { get; }
    }
}
