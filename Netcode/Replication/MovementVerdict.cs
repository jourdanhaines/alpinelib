namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The outcome of validating one reported move: what the server decided, and the state it intends to
    /// treat as authoritative from here.
    /// </summary>
    /// <remarks>
    /// The resolved state is always usable, whatever the verdict — accepted means the client's own state,
    /// clamped means a pulled-back version of it, rejected means the previous state unchanged. Callers
    /// therefore never branch on the kind to decide what to store; they branch on
    /// <see cref="RequiresCorrection"/> to decide whether to tell the client about it.
    /// </remarks>
    public readonly struct MovementVerdict {
        private readonly MovementVerdictKind kind;
        private readonly PawnState resolvedState;
        private readonly float reportedSpeed;
        private readonly float allowedSpeed;

        private MovementVerdict(MovementVerdictKind kind, in PawnState resolvedState, float reportedSpeed, float allowedSpeed) {
            this.kind = kind;
            this.resolvedState = resolvedState;
            this.reportedSpeed = reportedSpeed;
            this.allowedSpeed = allowedSpeed;
        }

        /// <summary>What the validator decided.</summary>
        public MovementVerdictKind Kind => kind;

        /// <summary>The state the server should now hold for this pawn.</summary>
        public PawnState ResolvedState => resolvedState;

        /// <summary>Horizontal speed the client's report implied, in metres per second.</summary>
        public float ReportedSpeed => reportedSpeed;

        /// <summary>Ceiling the gait allowed once tolerance was applied, in metres per second.</summary>
        public float AllowedSpeed => allowedSpeed;

        /// <summary>True when the client's own view differs from the resolved state and must be told.</summary>
        public bool RequiresCorrection => kind != MovementVerdictKind.Accepted;

        /// <summary>The move was legal.</summary>
        public static MovementVerdict Accept(in PawnState state, float reportedSpeed, float allowedSpeed) {
            return new MovementVerdict(MovementVerdictKind.Accepted, in state, reportedSpeed, allowedSpeed);
        }

        /// <summary>The move was slightly too far and has been pulled back.</summary>
        public static MovementVerdict Clamp(in PawnState state, float reportedSpeed, float allowedSpeed) {
            return new MovementVerdict(MovementVerdictKind.Clamped, in state, reportedSpeed, allowedSpeed);
        }

        /// <summary>The move was impossible and has been discarded.</summary>
        public static MovementVerdict Reject(in PawnState state, float reportedSpeed, float allowedSpeed) {
            return new MovementVerdict(MovementVerdictKind.Rejected, in state, reportedSpeed, allowedSpeed);
        }
    }
}
