using System;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// The client-side view of "who am I and which room am I in", supplied by the session layer.
    /// </summary>
    /// <remarks>
    /// Exists so the chat provider does not have to be told to re-target every time the session changes:
    /// it subscribes to <see cref="RoomChanged"/> and follows. A match launch keeps the same room key —
    /// a match is a phase of a session, not a new place — so in practice this fires on join and leave.
    /// </remarks>
    public interface IChatSessionContext {
        /// <summary>The local player's identity, as established by the session handshake.</summary>
        PlayerId LocalPlayerId { get; }

        /// <summary>The local player's display name.</summary>
        string LocalDisplayName { get; }

        /// <summary>Key of the room channel the local player belongs to. Empty when in no session.</summary>
        string CurrentRoomKey { get; }

        /// <summary>Raised with the new room key whenever the local player changes session.</summary>
        event Action<string> RoomChanged;
    }
}
