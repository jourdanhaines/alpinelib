namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Which step of a host or join refused, for the cases the client's own plumbing decided rather
    /// than the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SessionEndReason"/> answers a different question — why a <i>session</i> ended — and
    /// its values are a wire contract with the server. This one never leaves the process: it is how a
    /// menu tells "that is not a join code" from "this build has no server address" without matching on
    /// the text of <see cref="SessionJoinResult.Message"/>, which is a developer string that the next
    /// rewording silently breaks.
    /// </para>
    /// <para>
    /// <see cref="ServerRefused"/> is deliberately the default, so every existing
    /// <c>Denied(reason, message)</c> keeps meaning what it meant: the server said no and the message
    /// is the server's own copy. Append only, for the same reason the values a game switches over
    /// always are.
    /// </para>
    /// </remarks>
    public enum SessionDenial : byte {
        /// <summary>The server said no. <see cref="SessionJoinResult.Reason"/> carries its verdict.</summary>
        ServerRefused = 0,

        /// <summary>This build has no session configuration, so nothing could be attempted.</summary>
        NoSessionConfig = 1,

        /// <summary>What the player typed is not a join code.</summary>
        BadJoinCode = 2,

        /// <summary>What the player typed is not a server address.</summary>
        BadServerAddress = 3,

        /// <summary>Nothing could say where the server for this session is.</summary>
        NoServerEndpoint = 4,

        /// <summary>Hosting a local server process was asked for and none is configured.</summary>
        NoLocalServerConfig = 5,

        /// <summary>The network service produced no client facade to dial with.</summary>
        NoClientFacade = 6,

        /// <summary>The host attempt was cancelled before the server came up.</summary>
        HostStartCancelled = 7,

        /// <summary>The server this build launches beside itself did not start.</summary>
        HostStartFailed = 8
    }
}
