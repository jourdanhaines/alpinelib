using System;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Sessions {
    /// <summary>
    /// How a game plugs its own simulation into this server: one module per session, and one set of
    /// message handlers for the whole process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split is the same one <c>SessionRegistry</c> exists for. Several sessions share one socket and
    /// a router allows one handler per message id, so a game cannot register its ids per session — it
    /// registers them once, here, and the handler works out which session the sender is in through
    /// <c>resolveEntry</c> before touching anything.
    /// </para>
    /// <para>
    /// Ids a game claims must sit in <see cref="MessageIdBudget.GameBandStart"/> …
    /// <see cref="MessageIdBudget.GameBandEnd"/>. Anything else belongs to the library and will be taken
    /// out from under the game the next time the library grows a feature.
    /// </para>
    /// </remarks>
    public interface ISessionModuleFactory {
        /// <summary>
        /// Builds the module for one session. Called from the entry's constructor, so the entry's own
        /// pieces — host, replication, claims, spawner — are all readable and the session is not yet
        /// ticking.
        /// </summary>
        ISessionModule Create(SessionEntry entry);

        /// <summary>
        /// Claims this game's client-to-server message ids on the process-wide router, once, at startup.
        /// </summary>
        /// <param name="router">The one router every session's traffic arrives on.</param>
        /// <param name="resolveEntry">
        /// Which session a sender is attached to, or null while it is still at the front desk. A handler
        /// that skips this check is acting on a message from a peer in somebody else's session.
        /// </param>
        void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry);

        /// <summary>
        /// Which claims a session of this game will answer for, or null to accept every slot number.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asked <b>before</b> the entry exists, because the claim registry is one of the pieces the
        /// entry is built out of and an authority rule that arrives after the registry is live has
        /// already been wrong once. That is also why it is handed the <see cref="SessionHost"/> rather
        /// than the half-built entry: everything reachable from the host is real at this point, and
        /// nothing else is.
        /// </para>
        /// <para>
        /// The validator is called on the loop thread for every claim request off the wire, with the
        /// slot asked for, the peer that asked and the registry itself — so a rule about sibling slots
        /// ("one driver per train") can read <c>registry.Holders</c> to answer. It must not mutate the
        /// registry. A refused request is answered with <c>ClaimDenied</c>, so refusing is not silence.
        /// </para>
        /// <para>
        /// This governs the sessions of a server process, which is not every session of the game: a
        /// listen host runs no module factory at all. A game whose rule must hold in that mode too sets
        /// the same delegate on <c>SessionService.ClaimValidator</c>, which the in-process front desk
        /// passes to its registry.
        /// </para>
        /// </remarks>
        Func<ushort, PeerHandle, ServerClaimRegistry, bool> BuildClaimValidator(SessionHost host) => null;

        /// <summary>
        /// Which prefab an arriving member is spawned as, given the one the session config names.
        /// </summary>
        /// <remarks>
        /// Asked on every arrival, rejoins included, with the member already in the roster — so
        /// <see cref="SessionMember.AvatarData"/> is readable. A game with one character model leaves this
        /// alone; one with several maps the member's choice to that model's pawn prefab here.
        /// </remarks>
        ushort ResolvePawnPrefab(SessionMember member, ushort defaultPrefabId) => defaultPrefabId;
    }
}
