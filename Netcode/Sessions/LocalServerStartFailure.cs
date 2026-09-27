namespace AlpineLib.Netcode.Sessions {
    /// <summary>Why a server launched beside this build did not come up. Append only.</summary>
    public enum LocalServerStartFailure : byte {
        /// <summary>It started, or no local server was involved.</summary>
        None = 0,

        /// <summary>There is no server executable where the config says.</summary>
        Missing = 1,

        /// <summary>The preferred port is held by another process.</summary>
        PortInUse = 2,

        /// <summary>It exited before reporting readiness, for any other reason.</summary>
        Crashed = 3,

        /// <summary>It never reported readiness within the budget.</summary>
        Timeout = 4,

        /// <summary>The start was cancelled or superseded before it finished.</summary>
        Cancelled = 5
    }
}
