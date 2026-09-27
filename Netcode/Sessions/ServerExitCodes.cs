namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Process exit codes a dedicated server ends with, shared with the launcher that reads them.
    /// </summary>
    /// <remarks>
    /// A contract between two processes, so the values never move; a new ending gets a new number.
    /// </remarks>
    public static class ServerExitCodes {
        /// <summary>Stopped on request or by its idle window.</summary>
        public const int Clean = 0;

        /// <summary>The command line or the deployed configuration was unusable.</summary>
        public const int BadConfiguration = 1;

        /// <summary>The port it was asked for is held by another process.</summary>
        public const int PortInUse = 2;

        /// <summary>The game loop failed to start or stopped on an unhandled fault.</summary>
        public const int LoopFault = 3;
    }
}
