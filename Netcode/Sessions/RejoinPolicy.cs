namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Whether a member who lost transport may reclaim their roster slot, and for how long.
    /// </summary>
    /// <remarks>
    /// Wire byte; append only. While a rejoin is permitted the member is kept in the roster as
    /// disconnected, which reserves their <see cref="PlayerId"/> and display name.
    /// </remarks>
    public enum RejoinPolicy : byte {
        /// <summary>Losing transport removes the member immediately.</summary>
        None = 0,

        /// <summary>Slot is reserved for <c>rejoinWindowSeconds</c>, then swept.</summary>
        TimedWindow = 1,

        /// <summary>Slot is reserved for as long as the session lives (Penguin default).</summary>
        AnyTime = 2
    }
}
