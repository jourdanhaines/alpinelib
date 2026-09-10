using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Netcode.Sessions.Spawning {
    /// <summary>
    /// Decides where the next pawn of a session appears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from the spawner because "where" is the only part of spawning a game changes. A lobby
    /// with no authored markers wants a ring around the origin; a depot wants the platform beside the
    /// train. Both want the same despawn-then-spawn bookkeeping, and that lives in
    /// <see cref="SessionPawnSpawner"/>.
    /// </para>
    /// <para>
    /// Implementations are stateful by design: consecutive calls hand out different places rather than
    /// stacking a whole lobby on one spot. How far apart those places are is the implementation's own
    /// business, and a bounded one may still seat two pawns close enough to overlap, so this is not a
    /// separation guarantee. The state belongs to one session and nothing resets it, so a factory handing
    /// back a cached instance carries the last session's arrival count into the next and opens it already
    /// in overflow. Build one per session, as <c>SessionEntry</c> and <c>ListenServerFrontDesk</c> do.
    /// </para>
    /// <para>
    /// <b>Answer in world space under server authority.</b> A carrier-relative state is only meaningful for
    /// an owner-simulated pawn, so a server-authority session downgrades one to world space rather than
    /// letting replication refuse the spawn — the call happens inside a membership event, and a throw there
    /// leaves the member seated and announced with no body and no world. The numbers are kept as given, so
    /// a placement that means "two metres along this train car" must do the maths into world space itself.
    /// </para>
    /// </remarks>
    public interface ISpawnPlacement {
        /// <summary>
        /// The state the next pawn spawns in.
        /// </summary>
        /// <param name="member">Who is arriving. Placements that seat by party or by role read it.</param>
        /// <param name="isRejoin">
        /// True when the seat was already held and the link came back. The pawn is gone either way — a
        /// rejoin gets a fresh body — but a placement may want to put a returning player back where the
        /// rest of the session is rather than at the next free slot.
        /// </param>
        /// <param name="world">
        /// The collision the session simulates in, for a ground probe. Null is allowed and means the
        /// nominal spawn height stands.
        /// </param>
        /// <param name="serverTick">The tick the probe asks the movers about, so a moving floor answers for now.</param>
        PawnState NextSpawnState(SessionMember member, bool isRejoin, CollisionWorld world, uint serverTick);
    }
}
