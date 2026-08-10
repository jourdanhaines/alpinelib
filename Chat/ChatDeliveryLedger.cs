using System.Collections.Generic;

namespace AlpineLib.Chat {
    /// <summary>
    /// Remembers which message ids a client has already raised to its consumers, so a history push can
    /// be merged with live traffic without showing anything twice.
    /// </summary>
    /// <remarks>
    /// A joining client is subscribed to the channel before its history page is written, so the two
    /// streams overlap: a line said in that window arrives once as a broadcast and once inside the page.
    /// A plain "highest id seen" watermark cannot separate them, because the page is deliberately older
    /// than the watermark by the time it lands.
    /// <para>
    /// The ledger is bounded — the oldest id is forgotten once the window is full — because a session
    /// that runs for hours must not grow a set for every line ever said. Forgetting an id only risks a
    /// duplicate for a message older than the whole window, which no history push ever reaches back to.
    /// </para>
    /// </remarks>
    public sealed class ChatDeliveryLedger {
        private readonly int _capacity;
        private readonly Dictionary<ChatChannelId, ChannelWindow> _windows =
            new Dictionary<ChatChannelId, ChannelWindow>();

        /// <summary>Creates a ledger remembering <paramref name="capacity"/> ids per channel.</summary>
        public ChatDeliveryLedger(int capacity) {
            _capacity = capacity < 1 ? 1 : capacity;
        }

        /// <summary>How many ids per channel the ledger keeps before forgetting the oldest.</summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Records a delivery and reports whether it is new. Returns false — and changes nothing — when
        /// the id has already been delivered on that channel.
        /// </summary>
        public bool TryRecord(ChatChannelId channel, ulong messageId) {
            ChannelWindow window = GetOrCreateWindow(channel);
            return window.TryRecord(messageId);
        }

        /// <summary>True when the id has already been delivered on that channel.</summary>
        public bool HasDelivered(ChatChannelId channel, ulong messageId) {
            return _windows.TryGetValue(channel, out ChannelWindow window) && window.Contains(messageId);
        }

        /// <summary>Drops everything remembered for one channel.</summary>
        public void ForgetChannel(ChatChannelId channel) {
            _windows.Remove(channel);
        }

        /// <summary>Drops everything remembered, for every channel.</summary>
        public void Clear() {
            _windows.Clear();
        }

        private ChannelWindow GetOrCreateWindow(ChatChannelId channel) {
            if (_windows.TryGetValue(channel, out ChannelWindow existing)) {
                return existing;
            }

            var created = new ChannelWindow(_capacity);
            _windows.Add(channel, created);
            return created;
        }

        /// <summary>The recent-id window for one channel: a set for the test, a queue for the eviction.</summary>
        private sealed class ChannelWindow {
            private readonly int _capacity;
            private readonly HashSet<ulong> _delivered = new HashSet<ulong>();
            private readonly Queue<ulong> _order = new Queue<ulong>();

            public ChannelWindow(int capacity) {
                _capacity = capacity;
            }

            public bool Contains(ulong messageId) {
                return _delivered.Contains(messageId);
            }

            public bool TryRecord(ulong messageId) {
                if (!_delivered.Add(messageId)) {
                    return false;
                }

                _order.Enqueue(messageId);

                if (_order.Count > _capacity) {
                    _delivered.Remove(_order.Dequeue());
                }

                return true;
            }
        }
    }
}
