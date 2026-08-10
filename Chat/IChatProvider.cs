using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlpineLib.Chat {
    /// <summary>
    /// The whole of chat as the game sees it. Everything above this interface — the Unity chat service,
    /// the chat UI — knows only these members, which is what makes swapping the built-in provider for a
    /// hosted service a composition change rather than a rewrite.
    /// </summary>
    /// <remarks>
    /// The built-in implementation raises every event on the thread that pumps the transport (the Unity
    /// main thread, or the server game loop), inside <c>Poll</c>. There is no dispatcher anywhere in the
    /// stack, so an external provider that completes work on a background thread owes its consumers the
    /// same guarantee and must marshal before raising.
    /// <para>
    /// The async methods return work already in flight; a provider must never block the calling thread.
    /// </para>
    /// </remarks>
    public interface IChatProvider : IDisposable {
        /// <summary>Where the provider stands with its backend right now.</summary>
        ChatProviderState State { get; }

        /// <summary>What this provider supports beyond plain room chat.</summary>
        ChatProviderCapabilities Capabilities { get; }

        /// <summary>Raised whenever <see cref="State"/> changes.</summary>
        event Action<ChatProviderState> StateChanged;

        /// <summary>Raised for every line delivered to a channel the local player is in.</summary>
        event Action<ChatMessage> MessageReceived;

        /// <summary>Raised when channel membership changes, local or otherwise.</summary>
        event Action<ChatChannelEvent> ChannelChanged;

        /// <summary>Connects as the given identity and subscribes to its room channel.</summary>
        Task ConnectAsync(ChatIdentity identity, CancellationToken cancellationToken);

        /// <summary>Disconnects and drops every subscription. Safe to call when already disconnected.</summary>
        Task DisconnectAsync();

        /// <summary>
        /// Sends a line and completes when the server has ruled on it. The result says whether it landed
        /// and, when it did not, why — a rejection is a normal outcome, never an exception.
        /// </summary>
        Task<ChatSendResult> SendAsync(ChatChannelId channel, string text, CancellationToken cancellationToken);

        /// <summary>
        /// Fetches up to <paramref name="count"/> messages older than <paramref name="beforeMessageId"/>.
        /// Providers without <see cref="ChatProviderCapabilities.History"/> return an empty list.
        /// </summary>
        Task<IReadOnlyList<ChatMessage>> FetchHistoryAsync(
            ChatChannelId channel,
            ulong beforeMessageId,
            int count,
            CancellationToken cancellationToken);
    }
}
