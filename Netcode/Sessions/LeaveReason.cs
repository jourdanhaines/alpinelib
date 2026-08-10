namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Why a member left the roster, as broadcast on <c>MemberLeft</c>.
    /// </summary>
    /// <remarks>Wire byte; append only.</remarks>
    public enum LeaveReason : byte {
        /// <summary>Member asked to leave.</summary>
        Quit = 0,

        /// <summary>Member was kicked by the owner or an admin.</summary>
        Kicked = 1,

        /// <summary>Transport dropped. Under a rejoin policy the slot may still be reserved.</summary>
        TransportLost = 2,

        /// <summary>The whole session closed underneath the member.</summary>
        SessionClosed = 3
    }
}
