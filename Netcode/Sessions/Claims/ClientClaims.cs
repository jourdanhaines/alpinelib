using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions.Claims {
    /// <summary>
    /// The client's view of the session's claim slots: what the server last said about each one, and the
    /// two requests a client is allowed to make about them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is predicted. A claim is the one piece of session state where guessing is worse than
    /// waiting: two players reaching for the same lever would both see themselves take it, drive against
    /// each other for a round trip, and then have one of them yanked off. So a request changes nothing
    /// locally — the slot only moves when <c>ClaimChanged</c> comes back — and the UI a game builds on
    /// <see cref="OnClaimGranted"/> shows the truth a beat late rather than a lie immediately.
    /// </para>
    /// <para>
    /// Like <c>ClientReplication</c> this claims its router id in the constructor: a client has exactly
    /// one connection and one session, so there is no second instance to collide with.
    /// </para>
    /// <para>
    /// <see cref="LocalPeerId"/> is what separates "somebody holds it" from "I hold it", and it is only
    /// known once the server has put this client on a roster. Verdicts that arrive before it is set are
    /// still recorded, but raise no grant or loss — which is why whoever owns this object should adopt
    /// the peer id on the same event it adopts replication's.
    /// </para>
    /// </remarks>
    public sealed class ClientClaims : IDisposable {
        /// <summary>Holder id of a slot nobody holds, and the resting value of <see cref="LocalPeerId"/>.</summary>
        public const int FreeHolderPeerId = -1;

        private readonly NetClient client;
        private readonly Dictionary<ushort, int> holders = new Dictionary<ushort, int>();

        private bool disposed;

        /// <summary>Creates the view and starts listening for verdicts on the given connection.</summary>
        public ClientClaims(NetClient client) {
            this.client = client ?? throw new ArgumentNullException(nameof(client));

            LocalPeerId = FreeHolderPeerId;

            client.Router.Register<ClaimChanged>(ClaimMessageIds.ClaimChanged, HandleClaimChanged);
        }

        /// <summary>A slot changed hands: the slot, and its new holder or -1 when it went free.</summary>
        public event Action<ushort, int> OnClaimChanged;

        /// <summary>A slot became ours.</summary>
        public event Action<ushort> OnClaimGranted;

        /// <summary>A slot we were holding is no longer ours, whether we let it go or lost it.</summary>
        public event Action<ushort> OnClaimLost;

        /// <summary>Which peer this client is, or -1 before the server has said.</summary>
        public int LocalPeerId { get; set; }

        /// <summary>Every held slot and its holder. Free slots are absent rather than mapped to -1.</summary>
        public IReadOnlyDictionary<ushort, int> Holders => holders;

        /// <summary>Asks the server for a slot. Nothing changes here until the verdict arrives.</summary>
        public void RequestClaim(ushort slot) {
            var message = new ClaimRequest(slot);
            client.Send(ClaimMessageIds.ClaimRequest, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Gives up a slot. Ignored by the server unless we are the holder.</summary>
        public void Release(ushort slot) {
            var message = new ClaimRelease(slot);
            client.Send(ClaimMessageIds.ClaimRelease, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Peer id holding a slot, or -1 when it is free.</summary>
        public int Holder(ushort slot) {
            return holders.TryGetValue(slot, out int holder) ? holder : FreeHolderPeerId;
        }

        /// <summary>True when a specific peer holds the slot. A free slot is held by nobody, not by -1.</summary>
        public bool IsHeldBy(ushort slot, int peerId) {
            return peerId >= 0 && Holder(slot) == peerId;
        }

        /// <summary>True when this client holds the slot.</summary>
        public bool IsHeldLocally(ushort slot) {
            return IsHeldBy(slot, LocalPeerId);
        }

        /// <summary>
        /// Forgets every slot without raising anything. Used when leaving a session or before rebuilding
        /// on rejoin, where a loss event would be about a session that no longer exists.
        /// </summary>
        public void Clear() {
            holders.Clear();
        }

        /// <inheritdoc />
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            Clear();

            client.Router.Unregister(ClaimMessageIds.ClaimChanged);
        }

        /// <summary>
        /// Adopts one verdict. A verdict that agrees with what we already hold is dropped rather than
        /// replayed: a join keyframe re-states every held slot, and a client that had already heard about
        /// one must not be told it was granted a second time.
        /// </summary>
        private void HandleClaimChanged(in ClaimChanged message, PeerHandle sender) {
            int previousHolder = Holder(message.Slot);

            if (previousHolder == message.HolderPeerId) {
                return;
            }

            if (message.HolderPeerId < 0) {
                holders.Remove(message.Slot);
            }
            else {
                holders[message.Slot] = message.HolderPeerId;
            }

            OnClaimChanged?.Invoke(message.Slot, message.HolderPeerId);
            RaiseLocalTransition(message.Slot, previousHolder, message.HolderPeerId);
        }

        /// <summary>
        /// Turns a change of holder into the grant or loss this client cares about. Split out because a
        /// transition between two other peers is neither, and neither is one seen before we know who we
        /// are.
        /// </summary>
        private void RaiseLocalTransition(ushort slot, int previousHolder, int newHolder) {
            if (LocalPeerId < 0) {
                return;
            }

            if (newHolder == LocalPeerId) {
                OnClaimGranted?.Invoke(slot);
                return;
            }

            if (previousHolder == LocalPeerId) {
                OnClaimLost?.Invoke(slot);
            }
        }
    }
}
