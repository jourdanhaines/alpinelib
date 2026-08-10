namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// What happens to a session when its owner leaves.
    /// </summary>
    /// <remarks>
    /// Wire byte; append only. Sessions are server-hosted, so <see cref="TransferToMember"/> is a
    /// lobby-owner reassignment (broadcast as <c>OwnerChanged</c>), never a process migration.
    /// </remarks>
    public enum HostPolicy : byte {
        /// <summary>Owner leaving closes the session for everyone.</summary>
        EndSession = 0,

        /// <summary>Ownership passes to the next remaining member by join order.</summary>
        TransferToMember = 1
    }
}
