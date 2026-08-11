namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// One replicated thing: an id, the prefab that renders it, who owns it, who has authority over it,
    /// and its current state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same type is used on both ends, which is the point — the server holds the authoritative
    /// instance and the client holds its received copy, and neither side needs a translation layer to
    /// talk about the other's. What differs is only who writes <see cref="State"/>.
    /// </para>
    /// <para>
    /// <see cref="LastDirtyTick"/> is what makes snapshots cheap: an entity nobody moved is not written
    /// into the next snapshot at all. It is stamped by <see cref="ApplyState"/>, so it cannot be forgotten
    /// by a caller that mutates state some other way — there is no other way.
    /// </para>
    /// </remarks>
    public sealed class NetEntity {
        private PawnState state;

        /// <summary>Creates a pawn in its spawn state.</summary>
        public NetEntity(uint id, ushort prefabId, int ownerPeerId, AuthorityMode authority, in PawnState initialState)
            : this(id, prefabId, ownerPeerId, authority, EntityKind.Pawn, 0, in initialState) { }

        /// <summary>Creates an entity of any kind in its spawn state.</summary>
        public NetEntity(
            uint id,
            ushort prefabId,
            int ownerPeerId,
            AuthorityMode authority,
            EntityKind kind,
            ushort auxId,
            in PawnState initialState) {
            Id = id;
            PrefabId = prefabId;
            OwnerPeerId = ownerPeerId;
            Authority = authority;
            Kind = kind;
            AuxId = auxId;
            state = initialState;
            LastDirtyTick = 0u;
            LastAcknowledgedInputSequence = 0u;
            HighestReceivedInputSequence = 0u;
            StarvedTicks = 0;
        }

        /// <summary>Server-assigned identity, unique within a session and never reused while it lives.</summary>
        public uint Id { get; }

        /// <summary>Index into the prefab registry. A wire contract; the registry is append-only.</summary>
        public ushort PrefabId { get; }

        /// <summary>
        /// Peer whose player this entity belongs to, or a negative value for an unowned entity. Mutable
        /// because owner reassignment is a real event — a rejoining player reclaims their pawn under a
        /// new peer handle.
        /// </summary>
        public int OwnerPeerId { get; set; }

        /// <summary>Who simulates this entity. Fixed for the entity's lifetime; it rides on the spawn.</summary>
        public AuthorityMode Authority { get; }

        /// <summary>
        /// What sort of thing this is. Fixed for the entity's lifetime and carried on the spawn and every
        /// keyframe, because it decides what the client builds around the entity rather than merely how
        /// it looks.
        /// </summary>
        public EntityKind Kind { get; }

        /// <summary>
        /// Kind-specific identity: a mover's scene-authored mover id, zero for a pawn. It is how a client
        /// matches a replicated platform back to the path in its own copy of the scene geometry.
        /// </summary>
        public ushort AuxId { get; }

        /// <summary>The current state. Assign through <see cref="ApplyState"/> so dirt is tracked.</summary>
        public PawnState State => state;

        /// <summary>Tick at which the state last changed. Zero means it has not moved since it spawned.</summary>
        public uint LastDirtyTick { get; private set; }

        /// <summary>
        /// The owner's input sequence this state accounts for. Rides on every correction so the owner's
        /// prediction buffer knows exactly how much of its guesswork the server has now settled.
        /// </summary>
        public uint LastAcknowledgedInputSequence { get; set; }

        /// <summary>
        /// The highest input sequence ever accepted from the owner. Redundant input bundles legitimately
        /// re-deliver older sequences, and a sequenced-but-unreliable channel can replay a packet;
        /// applying the same input twice would move the pawn twice, so anything at or below this is
        /// dropped on receipt.
        /// </summary>
        public uint HighestReceivedInputSequence { get; set; }

        /// <summary>
        /// Consecutive ticks the server has simulated this pawn without a real input to consume. Drives
        /// the starvation decay: a short gap keeps the pawn moving on its last intent, a long one winds
        /// it down instead of inventing distance the owner never asked for.
        /// </summary>
        public int StarvedTicks { get; set; }

        /// <summary>The last input the server consumed for this entity, repeated when the stream stutters.</summary>
        public PawnInput LastInput { get; set; }

        /// <summary>True when <paramref name="sinceTick"/> is older than this entity's last change.</summary>
        public bool IsDirtySince(uint sinceTick) {
            return LastDirtyTick > sinceTick;
        }

        /// <summary>
        /// Writes a new state, stamping the tick only when the state actually moved.
        /// </summary>
        /// <remarks>
        /// The server steps every pawn every tick whether or not anyone is pressing anything, so
        /// unconditional stamping would mark a lobby full of idle penguins dirty forever and turn the
        /// dirty-tracked snapshot into a full one. Comparison is at wire resolution — see
        /// <see cref="PawnState.ApproximatelyEquals"/> — so a difference no peer could have received does
        /// not count as movement.
        /// </remarks>
        /// <returns>True when the state changed and the entity is now dirty.</returns>
        public bool ApplyState(in PawnState nextState, uint tick) {
            bool changed = !PawnState.ApproximatelyEquals(state, nextState);
            state = nextState;

            if (changed) {
                LastDirtyTick = tick;
            }

            return changed;
        }
    }
}
