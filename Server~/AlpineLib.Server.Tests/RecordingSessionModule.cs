using System.Collections.Generic;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Sessions;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A game module that does nothing but write down what the library told it, so a test can assert on
    /// the seam rather than on a game.
    /// </summary>
    internal sealed class RecordingSessionModule : ISessionModule {
        private readonly SessionEntry _entry;

        public RecordingSessionModule(SessionEntry entry) {
            _entry = entry;
        }

        /// <summary>The entry this module was created for.</summary>
        public SessionEntry Entry => _entry;

        /// <summary>How many times the loop stepped this module.</summary>
        public int TickCount { get; private set; }

        /// <summary>The last tick this module was stepped at.</summary>
        public uint LastServerTick { get; private set; }

        /// <summary>Peers announced as arriving, in order.</summary>
        public List<int> Joined { get; } = new List<int>();

        /// <summary>Peers announced as leaving, in order.</summary>
        public List<int> Left { get; } = new List<int>();

        /// <summary>Payloads that reached this module's own message handler.</summary>
        public List<byte> Received { get; } = new List<byte>();

        /// <summary>True once the entry disposed this module.</summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public void Tick(uint serverTick, float deltaSeconds) {
            TickCount++;
            LastServerTick = serverTick;
        }

        /// <inheritdoc />
        public void OnPeerJoined(PeerHandle peer) {
            Joined.Add(peer.Id);
        }

        /// <inheritdoc />
        public void OnPeerLeft(PeerHandle peer) {
            Left.Add(peer.Id);
        }

        /// <summary>Records a payload the factory's handler demultiplexed to this session.</summary>
        public void Receive(byte payload) {
            Received.Add(payload);
        }

        /// <inheritdoc />
        public void Dispose() {
            IsDisposed = true;
        }
    }
}
