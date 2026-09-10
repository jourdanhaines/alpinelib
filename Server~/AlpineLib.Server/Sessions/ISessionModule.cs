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
    /// </remarks>
    public interface ISessionModule : IDisposable {
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
        void OnPeerLeft(PeerHandle peer);
    }
}
