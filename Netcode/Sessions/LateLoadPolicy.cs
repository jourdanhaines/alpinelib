namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// What to do with a member who misses the match ready barrier.
    /// </summary>
    /// <remarks>Wire byte; append only.</remarks>
    public enum LateLoadPolicy : byte {
        /// <summary>Straggler stays a session member, sits out the match in the lobby scene.</summary>
        DropToLobby = 0,

        /// <summary>Straggler is disconnected from the session entirely.</summary>
        Disconnect = 1
    }
}
