using System;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Sessions {
    /// <summary>
    /// A game's own simulation, hung off one session and stepped with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of the game-to-library dependency on the server side. The library owns the
    /// socket, the roster, the world and the pawns; everything a particular game adds on top — a train
    /// consist, a scoreboard, a round timer — lives behind this interface and is created once per
    /// session by the game's <see cref="ISessionModuleFactory"/>.
    /// </para>
    /// <para>
    /// Every method runs on the game-loop thread, inside the tick, and nothing here is locked. A module
    /// that wants to be read from another thread posts through <c>GameThreadInbox</c> like everything
    /// else does.
    /// </para>
    /// <para>
    /// A throw out of any of these three closes <b>this</b> session and leaves every other one on the
    /// process running. That is deliberately narrower than the loop's own catch-all, which stops the
    /// box: a game that cannot seat one arrival has lost that lobby, not the server. The throw is
    /// logged with the session id and the session retires on the next sweep.
    /// </para>
    /// </remarks>
    public interface ISessionModule : IDisposable {
        /// <summary>
        /// The entry this module hangs off is finished, and the session has not ticked yet.
        /// </summary>
        /// <remarks>
        /// <see cref="ISessionModuleFactory.Create"/> runs with the entry still inside its own
        /// constructor: everything on it is readable, but the entry is not yet a thing anybody else
        /// holds, so a module that wants to publish itself — register with a game-wide index, seat the
        /// members already on the roster, put a first snapshot on the wire — cannot safely do it there.
        /// This is the moment after that. A throw here unwinds the entry exactly as a throw from
        /// <c>Create</c> does, so the session is refused rather than half-built.
        /// </remarks>
        void Attached() {
        }

        /// <summary>One step of the game's own simulation, after the session and its world have stepped.</summary>
        /// <param name="serverTick">The authoritative tick this step advances to.</param>
        /// <param name="deltaSeconds">Fixed step length, the same one the loop runs at.</param>
        void Tick(uint serverTick, float deltaSeconds);

        /// <summary>A connection was accepted into this session, after the roster already knows about it.</summary>
        void OnPeerJoined(PeerHandle peer);

        /// <summary>
        /// A connection is leaving this session, while it is still on the roster — a graceful leave and a
        /// dropped link both arrive here, and both arrive before the session retires the member.
        /// </summary>
        /// <remarks>
        /// The roster still knows this peer; the front desk no longer does. The
        /// <c>resolveEntry</c> a factory was handed in
        /// <see cref="ISessionModuleFactory.RegisterHandlers"/> returns null for it from here on, so a
        /// module that needs the session reaches for the entry it was created with instead.
        /// </remarks>
        void OnPeerLeft(PeerHandle peer);
    }
}
