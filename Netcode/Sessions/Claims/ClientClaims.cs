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
    /// known once the server has put this client on a roster. A verdict that lands before then is recorded
    /// but belongs to nobody in particular; learning the id re-reads what is already held and raises the
    /// grants that were waiting on it, so a game may adopt the id whenever its own plumbing knows it
    /// instead of having to beat the first verdict to the pump.
    /// </para>
    /// </remarks>
    public sealed class ClientClaims : IDisposable {
        /// <summary>Holder id of a slot nobody holds, and the resting value of <see cref="LocalPeerId"/>.</summary>
        public const int FreeHolderPeerId = -1;

        private readonly NetClient client;
        private readonly Dictionary<ushort, int> holders = new Dictionary<ushort, int>();

        private int localPeerId = FreeHolderPeerId;
        private bool disposed;

        /// <summary>Creates the view and starts listening for verdicts on the given connection.</summary>
        public ClientClaims(NetClient client) {
            this.client = client ?? throw new ArgumentNullException(nameof(client));

            client.Router.Register<ClaimChanged>(ClaimMessageIds.ClaimChanged, HandleClaimChanged);
        }

        /// <summary>A slot changed hands: the slot, and its new holder or -1 when it went free.</summary>
        public event Action<ushort, int> OnClaimChanged;

        /// <summary>A slot became ours.</summary>
        public event Action<ushort> OnClaimGranted;

        /// <summary>A slot we were holding is no longer ours, whether we let it go or lost it.</summary>
        public event Action<ushort> OnClaimLost;

        /// <summary>Which peer this client is, or -1 before the server has said.</summary>
        /// <remarks>
        /// Setting this re-reads the map: slots already held by the new id are granted, slots held by the
        /// old one are lost. Without that a verdict that arrived before the roster did would leave the
        /// client silently holding a lever it was never told it had.
        /// </remarks>
        public int LocalPeerId {
            get => localPeerId;
            set => AdoptLocalPeerId(value);
        }

        /// <summary>Every held slot and its holder. Free slots are absent rather than mapped to -1.</summary>
        /// <remarks>The live map, not a copy: mutating the view while walking it throws.</remarks>
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
        /// Forgets every slot, reporting the ones we were holding as lost.
        /// </summary>
        /// <remarks>
        /// Leaving a session is a loss like any other from the game's side: a player standing at a lever
        /// is disengaged by <see cref="OnClaimLost"/>, and staying silent here because the session is over
        /// would leave that player welded to a control that no longer exists. The map is emptied before
        /// the first event, so a handler that reads the view sees the session already gone.
        /// </remarks>
        public void Clear() {
            List<ushort> lost = LocallyHeldSlots();
            holders.Clear();

            RaiseSlots(OnClaimLost, lost);
        }

        /// <summary>
        /// Stops listening and drops the view, reporting every slot we were holding as lost.
        /// </summary>
        /// <remarks>
        /// A teardown reaches the game the same way a kick does — through <see cref="OnClaimLost"/> — so
        /// whoever is standing at a lever is disengaged by the same path either way and needs no separate
        /// shutdown hook of its own.
        /// </remarks>
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
            if (localPeerId < 0) {
                return;
            }

            if (newHolder == localPeerId) {
                OnClaimGranted?.Invoke(slot);
                return;
            }

            if (previousHolder == localPeerId) {
                OnClaimLost?.Invoke(slot);
            }
        }

        /// <summary>
        /// Takes on a new identity and re-reads the map through it, so that slots already recorded turn
        /// into the grants and losses the game would have heard had the id been known all along.
        /// </summary>
        private void AdoptLocalPeerId(int peerId) {
            if (peerId == localPeerId) {
                return;
            }

            List<ushort> lost = LocallyHeldSlots();
            localPeerId = peerId;
            List<ushort> granted = LocallyHeldSlots();

            RaiseSlots(OnClaimLost, lost);
            RaiseSlots(OnClaimGranted, granted);
        }

        /// <summary>
        /// Every slot the current identity holds, snapshotted: an event handler may claim, release or
        /// change identity again from inside the walk that follows.
        /// </summary>
        private List<ushort> LocallyHeldSlots() {
            var slots = new List<ushort>();

            if (localPeerId < 0) {
                return slots;
            }

            foreach (KeyValuePair<ushort, int> held in holders) {
                if (held.Value == localPeerId) {
                    slots.Add(held.Key);
                }
            }

            return slots;
        }

        /// <summary>Raises one event over a snapshot of slots.</summary>
        private void RaiseSlots(Action<ushort> handler, List<ushort> slots) {
            if (handler == null) {
                return;
            }

            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++) {
                handler(slots[slotIndex]);
            }
        }
    }
}
