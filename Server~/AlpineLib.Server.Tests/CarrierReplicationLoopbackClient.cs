using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// One player's end of a replication session: a real connection, a real <see cref="ClientReplication"/>
    /// over it, and a log of the corrections the server pushed back.
    /// </summary>
    internal sealed class CarrierReplicationLoopbackClient : IDisposable {
        private readonly SessionLoopbackTransport _transport;
        private readonly NetClient _client;
        private readonly ClientReplication _replication;
        private readonly List<PawnState> _corrections = new List<PawnState>();

        private bool _isDisposed;

        public CarrierReplicationLoopbackClient(SessionLoopbackTransport transport, NetConfig config) {
            _transport = transport;
            _client = new NetClient(transport, config);
            _replication = new ClientReplication(_client, config);
            _replication.OnAuthorityCorrected += RecordCorrection;
        }

        /// <summary>This client's view of the world.</summary>
        public ClientReplication Replication => _replication;

        /// <summary>How the server addresses this client.</summary>
        public PeerHandle ServerSidePeer { get; private set; } = PeerHandle.None;

        /// <summary>True once the dial has completed.</summary>
        public bool IsConnected => _client.IsConnected;

        /// <summary>Every corrected state the server sent back, in order.</summary>
        public IReadOnlyList<PawnState> Corrections => _corrections;

        /// <summary>Dials the one server on the loopback network.</summary>
        public void Connect() {
            _client.Connect(NetEndpoint.Direct("loopback", 1));
        }

        /// <summary>Adopts the peer id the server assigned, the way the session service does.</summary>
        public void BindPeerId(PeerHandle serverSidePeer) {
            ServerSidePeer = serverSidePeer;
            _replication.LocalPeerId = serverSidePeer.Id;
        }

        /// <summary>One pump of this client's connection and render clock.</summary>
        public void Update(float deltaSeconds) {
            _client.Update(deltaSeconds);
            _replication.Tick(deltaSeconds);
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_isDisposed) {
                return;
            }

            _isDisposed = true;
            _replication.OnAuthorityCorrected -= RecordCorrection;
            _replication.Dispose();
            _client.Dispose();
            _transport.Dispose();
        }

        private void RecordCorrection(NetEntity entity, PawnState state) {
            _corrections.Add(state);
        }
    }
}
