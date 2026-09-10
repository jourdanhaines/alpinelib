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
    /// Implementations are stateful by design: consecutive calls are expected to hand out different
    /// places, so two players joining in the same tick never stand inside each other.
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
