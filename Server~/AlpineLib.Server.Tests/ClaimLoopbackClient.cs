using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// One player's end of a claim session: a real connection, a real <see cref="ClientClaims"/> over it,
    /// and a log of everything the view raised.
    /// </summary>
    /// <remarks>
    /// The event logs are what most claim assertions are actually about. Whether a slot ends up held is
    /// easy to get right by accident; whether the holder was told once that it was granted, and the loser
    /// told nothing at all, is the part that breaks.
    /// </remarks>
    internal sealed class ClaimLoopbackClient : IDisposable {
        private readonly SessionLoopbackTransport _transport;
        private readonly NetClient _client;
        private readonly ClientClaims _claims;
        private readonly List<ClaimVerdict> _verdicts = new List<ClaimVerdict>();
        private readonly List<ushort> _granted = new List<ushort>();
        private readonly List<ushort> _lost = new List<ushort>();

        private bool _isDisposed;

        public ClaimLoopbackClient(SessionLoopbackTransport transport, NetConfig config) {
            _transport = transport;
            _client = new NetClient(transport, config);
            _claims = new ClientClaims(_client);

            _claims.OnClaimChanged += RecordClaimChanged;
            _claims.OnClaimGranted += RecordGranted;
            _claims.OnClaimLost += RecordLost;
        }

        /// <summary>This client's view of the session's slots.</summary>
        public ClientClaims Claims => _claims;

        /// <summary>The transport underneath, for tests that need to sever the link.</summary>
        public SessionLoopbackTransport Transport => _transport;

        /// <summary>How the server addresses this client.</summary>
        public PeerHandle ServerSidePeer { get; private set; } = PeerHandle.None;

        /// <summary>True once the dial has completed.</summary>
        public bool IsConnected => _client.IsConnected;

        /// <summary>Every verdict the view raised, in order.</summary>
        public IReadOnlyList<ClaimVerdict> Verdicts => _verdicts;

        /// <summary>Every slot the view said became ours, in order.</summary>
        public IReadOnlyList<ushort> Granted => _granted;

        /// <summary>Every slot the view said stopped being ours, in order.</summary>
        public IReadOnlyList<ushort> Lost => _lost;

        /// <summary>Dials the one server on the loopback network.</summary>
        public void Connect() {
            _client.Connect(NetEndpoint.Direct("loopback", 1));
        }

        /// <summary>
        /// Adopts the peer id the server assigned, the way the session service does once the roster
        /// arrives. Until this runs the view records verdicts but knows none of them are about us.
        /// </summary>
        public void BindPeerId(PeerHandle serverSidePeer) {
            ServerSidePeer = serverSidePeer;
            _claims.LocalPeerId = serverSidePeer.Id;
        }

        /// <summary>One pump of this client's connection.</summary>
        public void Update(float deltaSeconds) {
            _client.Update(deltaSeconds);
        }

        /// <summary>Forgets every recorded event, so a test can assert on one phase at a time.</summary>
        public void ClearLog() {
            _verdicts.Clear();
            _granted.Clear();
            _lost.Clear();
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_isDisposed) {
                return;
            }

            _isDisposed = true;

            _claims.OnClaimChanged -= RecordClaimChanged;
            _claims.OnClaimGranted -= RecordGranted;
            _claims.OnClaimLost -= RecordLost;
            _claims.Dispose();
            _client.Dispose();
            _transport.Dispose();
        }

        private void RecordClaimChanged(ushort slot, int holderPeerId) {
            _verdicts.Add(new ClaimVerdict(slot, holderPeerId));
        }

        private void RecordGranted(ushort slot) {
            _granted.Add(slot);
        }

        private void RecordLost(ushort slot) {
            _lost.Add(slot);
        }
    }
}
