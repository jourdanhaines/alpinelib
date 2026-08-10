namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// Who decides where a networked pawn actually is.
    /// </summary>
    /// <remarks>
    /// Wire byte; append only — it rides on <c>SpawnEntity</c>. <see cref="Server"/> is the default:
    /// the owning client sends <c>InputCommand</c>, the server steps the shared motor, and the owner
    /// predicts locally and reconciles on <c>AuthorityCorrection</c>. <see cref="OwnerClient"/> is the
    /// opt-out where the owner sends <c>PawnState</c> directly and the server only validates it.
    /// </remarks>
    public enum AuthorityMode : byte {
        /// <summary>Server simulates from client input. Default.</summary>
        Server = 0,

        /// <summary>Owning client simulates and reports state; the server validates.</summary>
        OwnerClient = 1
    }
}
