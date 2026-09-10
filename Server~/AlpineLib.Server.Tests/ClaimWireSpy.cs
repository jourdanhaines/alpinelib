using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A connected peer that records every <c>ClaimChanged</c> the server sends it, without folding any
    /// of them into a view.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientClaims"/> deliberately swallows a verdict that agrees with what it already holds,
    /// which is exactly the behaviour a keyframe test must not rely on: "the server sent one message per
    /// held slot and nothing for the free ones" is a claim about the wire, not about the view.
    /// </remarks>
    internal sealed class ClaimWireSpy : IDisposable {
        private readonly SessionLoopbackTransport _transport;
        private readonly NetClient _client;
        private readonly List<ClaimVerdict> _received = new List<ClaimVerdict>();

        private bool _isDisposed;

        public ClaimWireSpy(SessionLoopbackTransport transport, NetConfig config) {
            _transport = transport;
            _client = new NetClient(transport, config);
            _client.Router.Register<ClaimChanged>(ClaimMessageIds.ClaimChanged, RecordClaimChanged);
        }

        /// <summary>How the server addresses this peer.</summary>
        public PeerHandle ServerSidePeer { get; private set; } = PeerHandle.None;

        /// <summary>True once the dial has completed.</summary>
        public bool IsConnected => _client.IsConnected;

        /// <summary>Every verdict that arrived, verbatim and in order.</summary>
        public IReadOnlyList<ClaimVerdict> Received => _received;

        /// <summary>Dials the one server on the loopback network.</summary>
        public void Connect() {
            _client.Connect(NetEndpoint.Direct("loopback", 1));
        }

        /// <summary>Records the handle the server assigned.</summary>
        public void BindPeerId(PeerHandle serverSidePeer) {
            ServerSidePeer = serverSidePeer;
        }

        /// <summary>One pump of this peer's connection.</summary>
        public void Update(float deltaSeconds) {
            _client.Update(deltaSeconds);
        }

        /// <summary>Forgets everything received, so a test can assert on one phase at a time.</summary>
        public void ClearLog() {
            _received.Clear();
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_isDisposed) {
                return;
            }

            _isDisposed = true;
            _client.Router.Unregister(ClaimMessageIds.ClaimChanged);
            _client.Dispose();
            _transport.Dispose();
        }

        private void RecordClaimChanged(in ClaimChanged message, PeerHandle sender) {
            _received.Add(new ClaimVerdict(message.Slot, message.HolderPeerId));
        }
    }
}
