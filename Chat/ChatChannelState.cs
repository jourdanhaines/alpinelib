using System.Collections.Generic;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// One channel as the server knows it: who is subscribed, what the next message id is, and the last
    /// few lines said in it.
    /// </summary>
    /// <remarks>
    /// The message counter lives here rather than on the service because ordering is a per-channel
    /// promise, not a server-wide one. Two channels handing out ids from a shared counter would leave
    /// gaps in each channel's sequence, and a client merging a history push with live traffic uses
    /// exactly that sequence to decide what it has already seen.
    /// <para>
    /// History is a fixed-capacity ring rather than a growing list. A session that runs for hours must
    /// cost the same memory as one that just started, and nothing above ever asks for more than the
    /// buffer holds — deep history, if it is ever wanted, belongs in a backend behind
    /// <see cref="IChatProvider"/>, not in the game server's heap.
    /// </para>
    /// </remarks>
    public sealed class ChatChannelState {
        private readonly ChatChannelId _channel;
        private readonly List<PlayerId> _subscribers = new List<PlayerId>();
        private readonly ChatMessage[] _history;

        private int _oldestIndex;
        private int _historyCount;
        private ulong _lastMessageId;

        /// <summary>Creates an empty channel whose history holds <paramref name="historyCapacity"/> lines.</summary>
        public ChatChannelState(ChatChannelId channel, int historyCapacity) {
            _channel = channel;
            _history = new ChatMessage[historyCapacity < 1 ? 1 : historyCapacity];
        }

        /// <summary>Which channel this is.</summary>
        public ChatChannelId Channel => _channel;

        /// <summary>Everyone subscribed right now, connected or not.</summary>
        public IReadOnlyList<PlayerId> Subscribers => _subscribers;

        /// <summary>How many lines the history currently holds.</summary>
        public int HistoryCount => _historyCount;

        /// <summary>Most recent id handed out, or zero when nothing has been said yet.</summary>
        public ulong LastMessageId => _lastMessageId;

        /// <summary>Adds a subscriber. Returns false when they were already subscribed.</summary>
        public bool Subscribe(PlayerId player) {
            if (_subscribers.Contains(player)) {
                return false;
            }

            _subscribers.Add(player);
            return true;
        }

        /// <summary>Removes a subscriber. Returns false when they were not subscribed.</summary>
        public bool Unsubscribe(PlayerId player) {
            return _subscribers.Remove(player);
        }

        /// <summary>True when the player is allowed to send to and receive from this channel.</summary>
        public bool IsSubscribed(PlayerId player) {
            return _subscribers.Contains(player);
        }

        /// <summary>Hands out the next id in this channel's sequence.</summary>
        public ulong NextMessageId() {
            _lastMessageId++;
            return _lastMessageId;
        }

        /// <summary>Records a delivered line, evicting the oldest once the ring is full.</summary>
        public void Append(ChatMessage message) {
            if (message == null) {
                return;
            }

            if (_historyCount < _history.Length) {
                _history[(_oldestIndex + _historyCount) % _history.Length] = message;
                _historyCount++;
                return;
            }

            _history[_oldestIndex] = message;
            _oldestIndex = (_oldestIndex + 1) % _history.Length;
        }

        /// <summary>
        /// Copies the newest <paramref name="count"/> lines into <paramref name="destination"/>, oldest
        /// first — the shape a joining client can append straight into its view.
        /// </summary>
        public void CopyRecent(int count, List<ChatMessage> destination) {
            CopyBefore(0uL, count, destination);
        }

        /// <summary>
        /// Copies up to <paramref name="count"/> lines older than <paramref name="beforeMessageId"/>,
        /// oldest first. A <paramref name="beforeMessageId"/> of zero means "the newest ones".
        /// </summary>
        public void CopyBefore(ulong beforeMessageId, int count, List<ChatMessage> destination) {
            if (destination == null || count < 1) {
                return;
            }

            int lastEligible = FindLastIndexBefore(beforeMessageId);

            if (lastEligible < 0) {
                return;
            }

            int available = lastEligible + 1;
            int taken = count < available ? count : available;

            for (int offset = available - taken; offset <= lastEligible; offset++) {
                destination.Add(_history[(_oldestIndex + offset) % _history.Length]);
            }
        }

        /// <summary>Index into the ring, oldest-first, of the newest line below the given id.</summary>
        private int FindLastIndexBefore(ulong beforeMessageId) {
            if (beforeMessageId == 0uL) {
                return _historyCount - 1;
            }

            for (int offset = _historyCount - 1; offset >= 0; offset--) {
                ChatMessage candidate = _history[(_oldestIndex + offset) % _history.Length];

                if (candidate != null && candidate.MessageId < beforeMessageId) {
                    return offset;
                }
            }

            return -1;
        }
    }
}
