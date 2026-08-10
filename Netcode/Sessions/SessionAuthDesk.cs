using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The authentication half of a front desk: it turns <c>AuthRequest</c> into a proven
    /// <see cref="PlayerIdentity"/> without ever blocking the tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IAuthValidator.ValidateAsync"/> may take a network round trip of its own — a Steam
    /// ticket check, a backend lookup — and the simulation cannot wait on it. So nothing here is ever
    /// awaited inline: the validation runs wherever the validator puts it, and its completion is posted
    /// to the server's <see cref="Timing.TickInbox"/>. Every mutation of this desk's state, and every
    /// byte it sends, therefore happens on the tick thread inside <c>NetServer.Update</c> — the same
    /// thread the sessions run on, with no locks anywhere.
    /// </para>
    /// <para>
    /// A peer that clears this desk is authenticated and attached to nothing. Deciding which
    /// <see cref="SessionHost"/> it goes to is the <see cref="ISessionFrontDesk"/>'s job.
    /// </para>
    /// </remarks>
    public sealed class SessionAuthDesk {
        private readonly NetServer _server;
        private readonly IAuthValidator _validator;
        private readonly Dictionary<int, PlayerIdentity> _identityByPeerId = new Dictionary<int, PlayerIdentity>();

        private int _pendingCount;

        public SessionAuthDesk(NetServer server, IAuthValidator validator) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <summary>A peer proved who it is. Raised on the tick thread, never from the validator's thread.</summary>
        public event Action<PeerHandle, PlayerIdentity> OnPeerAuthenticated;

        /// <summary>A peer failed to prove who it is, with the reason already sent to it.</summary>
        public event Action<PeerHandle, string> OnPeerRejected;

        /// <summary>How many validations are in flight. Only meaningful when read on the tick thread.</summary>
        public int PendingCount => _pendingCount;

        /// <summary>How many peers are currently authenticated.</summary>
        public int AuthenticatedCount => _identityByPeerId.Count;

        /// <summary>Claims the <c>AuthRequest</c> id on a router. Convenience for front desks that want it.</summary>
        public void RegisterHandlers(MessageRouter router) {
            if (router == null) {
                throw new ArgumentNullException(nameof(router));
            }

            router.Register<AuthRequest>(SessionMessageIds.AuthRequest, HandleAuthRequest);
        }

        /// <summary>
        /// Starts validating a peer's claim. Returns immediately; the verdict lands on a later tick.
        /// </summary>
        public void HandleAuthRequest(in AuthRequest request, PeerHandle sender) {
            if (_identityByPeerId.ContainsKey(sender.Id)) {
                Reject(sender, "This connection is already authenticated.");
                return;
            }

            PlayerIdentity claimed = request.ToIdentity();
            AuthCredentials credentials = new AuthCredentials(request.AuthMethod, request.Token);
            BeginValidation(sender, claimed, credentials);
        }

        /// <summary>The identity a peer proved, when it has proved one.</summary>
        public bool TryGetIdentity(PeerHandle peer, out PlayerIdentity identity) {
            return _identityByPeerId.TryGetValue(peer.Id, out identity);
        }

        /// <summary>True once the peer has cleared the desk.</summary>
        public bool IsAuthenticated(PeerHandle peer) {
            return _identityByPeerId.ContainsKey(peer.Id);
        }

        /// <summary>Drops a peer's identity. Call when its connection ends.</summary>
        public void Forget(PeerHandle peer) {
            _identityByPeerId.Remove(peer.Id);
        }

        /// <summary>Drops every identity. For shutdown paths.</summary>
        public void Clear() {
            _identityByPeerId.Clear();
        }

        private void BeginValidation(PeerHandle peer, PlayerIdentity claimed, AuthCredentials credentials) {
            PendingValidation pending = new PendingValidation(this, peer);
            _pendingCount++;

            try {
                pending.Begin(_validator.ValidateAsync(claimed, credentials, CancellationToken.None));
            }
            catch (Exception) {
                // A validator that throws before it ever returns a task has failed this peer, not the
                // server: report it the same way a rejecting verdict would be reported and carry on.
                _pendingCount--;
                Reject(peer, "Authentication failed.");
            }
        }

        private void PostCompletion(Action completion) {
            _server.Inbox.Post(completion);
        }

        private void CompleteValidation(PeerHandle peer, Task<AuthVerdict> validation) {
            _pendingCount--;

            if (validation.IsFaulted || validation.IsCanceled) {
                Reject(peer, "Authentication failed.");
                return;
            }

            AuthVerdict verdict = validation.Result;
            if (!verdict.IsAccepted) {
                Reject(peer, verdict.Reason);
                return;
            }

            PlayerIdentity identity = verdict.Identity ?? new PlayerIdentity();
            _identityByPeerId[peer.Id] = identity;

            AuthResponse response = AuthResponse.Accept(peer.Id);
            _server.Send(peer, SessionMessageIds.AuthResponse, in response, DeliveryClass.ReliableOrdered);
            OnPeerAuthenticated?.Invoke(peer, identity);
        }

        private void Reject(PeerHandle peer, string reason) {
            string detail = reason ?? string.Empty;
            AuthResponse response = AuthResponse.Reject(detail);
            _server.Send(peer, SessionMessageIds.AuthResponse, in response, DeliveryClass.ReliableOrdered);
            OnPeerRejected?.Invoke(peer, detail);
        }

        /// <summary>
        /// One in-flight validation. It exists so the hop from the validator's thread to the tick thread
        /// is two plain method groups rather than a pair of closures capturing mutable desk state.
        /// </summary>
        private sealed class PendingValidation {
            private readonly SessionAuthDesk _desk;
            private readonly PeerHandle _peer;

            private Task<AuthVerdict> _validation;

            public PendingValidation(SessionAuthDesk desk, PeerHandle peer) {
                _desk = desk;
                _peer = peer;
            }

            /// <summary>Attaches to the validator's task. Nothing about it is awaited.</summary>
            public void Begin(Task<AuthVerdict> validation) {
                _validation = validation ?? Task.FromResult(AuthVerdict.Reject("Validator returned no result."));
                _validation.ContinueWith(Publish, TaskContinuationOptions.ExecuteSynchronously);
            }

            private void Publish(Task<AuthVerdict> completed) {
                _desk.PostCompletion(Deliver);
            }

            private void Deliver() {
                _desk.CompleteValidation(_peer, _validation);
            }
        }
    }
}
