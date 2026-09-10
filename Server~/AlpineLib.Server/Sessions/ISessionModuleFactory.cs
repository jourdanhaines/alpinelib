using System;
using AlpineLib.Netcode.Protocol;
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
    }
}
