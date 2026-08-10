using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// Where the chat pipeline reports what it did, so moderation and audit logging can live outside the
    /// pipeline itself.
    /// </summary>
    /// <remarks>
    /// Both callbacks fire on the game-loop thread and must return promptly; an implementation that
    /// wants to write to a database queues the work rather than doing it here.
    /// </remarks>
    public interface IChatModerationSink {
        /// <summary>A line was accepted and broadcast.</summary>
        void OnMessageDelivered(ChatMessage message);

        /// <summary>A line was refused, with the ruling that refused it.</summary>
        void OnMessageRejected(PlayerId sender, ChatChannelId channel, string text, ChatFilterResult result);

        /// <summary>A player was muted, automatically or by an operator.</summary>
        void OnPlayerMuted(PlayerId player, long untilUnixMs, string reason);
    }
}
