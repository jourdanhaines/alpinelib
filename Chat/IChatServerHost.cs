using System;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// Everything the chat pipeline needs to know about the session hosting it: who is in the room, what
    /// they are called, and what time the server thinks it is.
    /// </summary>
    /// <remarks>
    /// The adapter over <c>SessionHost</c> implements this, which keeps the dependency pointing one way:
    /// chat leans on the session, the session never leans on chat. Rooms are keyed by string rather than
    /// by session object so this interface stays implementable by a test double.
    /// </remarks>
    public interface IChatServerHost {
        /// <summary>Server clock in Unix milliseconds — the single source of message timestamps.</summary>
        long ServerTimeUnixMs { get; }

        /// <summary>Raised when a player becomes a member of a room, including on rejoin.</summary>
        event Action<PlayerId, string> PlayerJoinedRoom;

        /// <summary>Raised when a player stops being a member of a room.</summary>
        event Action<PlayerId, string> PlayerLeftRoom;

        /// <summary>
        /// Raised when a player's connection drops while their membership is retained for a rejoin.
        /// Chat stops delivering to them but keeps their history slot.
        /// </summary>
        event Action<PlayerId> PlayerDisconnected;

        /// <summary>The name to stamp on the player's messages. Empty when the player is unknown.</summary>
        string GetDisplayName(PlayerId player);

        /// <summary>True when the player currently has a live connection.</summary>
        bool IsOnline(PlayerId player);
    }
}
