using System;

namespace AlpineLib.Chat {
    /// <summary>
    /// What a chat provider can actually do. The UI queries this rather than hard-coding what the
    /// built-in provider happens to support, so swapping in an external service that offers whispers or
    /// server-side history lights those affordances up without a code change.
    /// </summary>
    [Flags]
    public enum ChatProviderCapabilities {
        /// <summary>Send and receive on the room channel, nothing more.</summary>
        None = 0,

        /// <summary>Older messages can be fetched on demand, not just pushed on join.</summary>
        History = 1 << 0,

        /// <summary>One-to-one channels are routable.</summary>
        Whisper = 1 << 1,

        /// <summary>Party channels are routable.</summary>
        Party = 1 << 2,

        /// <summary>A server-wide channel exists.</summary>
        Global = 1 << 3,

        /// <summary>Mutes and reports are handled by the provider rather than the game server.</summary>
        Moderation = 1 << 4,

        /// <summary>Channel membership changes are reported as they happen.</summary>
        Presence = 1 << 5
    }
}
