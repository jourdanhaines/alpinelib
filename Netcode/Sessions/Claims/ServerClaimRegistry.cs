using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions.Claims {
    /// <summary>
    /// The authority over one session's claim slots: at most one peer may hold a slot, and every change
    /// of holder is published to that session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What a slot is.</b> A slot is a numbered, exclusive right the game defines — a driver's lever,
    /// a turret, a workstation. The library deliberately knows nothing about what a number means: it only
    /// guarantees that one peer at a time owns it, that the owner is the only one who can give it up, and
    /// that a disconnect never leaves a slot held by somebody who is gone. Anything richer — queues,
    /// priorities, per-slot permissions — belongs to the game on top of this.
    /// </para>
    /// <para>
    /// <b>One per session, not one per server.</b> Every broadcast goes through <see cref="Peers"/> — the
    /// session's own member list — so a process hosting several lobbies keeps their slots apart, and a
    /// request from a peer that is not in that list is dropped rather than acted on. Without that check a
    /// connection authenticated but attached to another session could take this one's levers.
    /// </para>
    /// <para>
    /// <b>Router registration is opt-in</b>, for the same reason it is on <c>ServerReplication</c>: a
    /// <see cref="Protocol.MessageRouter"/> allows one handler per id, so several sessions sharing a
    /// server cannot each claim <c>ClaimRequest</c>. A single-session host calls
    /// <see cref="AttachToRouter"/>; a multi-session front desk registers once itself, resolves which
    /// session a peer belongs to, and calls <see cref="HandleClaimRequest"/> and
    /// <see cref="HandleClaimRelease"/> on the right registry.
    /// </para>
    /// </remarks>
    public sealed class ServerClaimRegistry {
        /// <summary>Holder id of a slot nobody holds. Matches <see cref="PeerHandle.None"/>.</summary>
        public const int FreeHolderPeerId = -1;

        private readonly NetServer server;
        private readonly Func<IReadOnlyList<PeerHandle>> peerSource;
        private readonly Dictionary<ushort, int> holders = new Dictionary<ushort, int>();

        private bool isAttachedToRouter;

        /// <summary>Creates the registry for one session's slots.</summary>
        /// <param name="server">The server facade the verdicts are broadcast through.</param>
        /// <param name="peerSource">
        /// The session's connected peers, re-read on every send. It is a delegate rather than a list so
        /// that a roster change is seen without the registry having to be told about it.
        /// </param>
        public ServerClaimRegistry(NetServer server, Func<IReadOnlyList<PeerHandle>> peerSource) {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.peerSource = peerSource ?? throw new ArgumentNullException(nameof(peerSource));
        }

        /// <summary>A slot changed hands: the slot, and its new holder or -1 when it went free.</summary>
        public event Action<ushort, int> OnClaimChanged;

        /// <summary>Who this registry broadcasts to — the session's members, and nobody else.</summary>
        public IReadOnlyList<PeerHandle> Peers => peerSource() ?? Array.Empty<PeerHandle>();

        /// <summary>Every held slot and its holder. Free slots are absent rather than mapped to -1.</summary>
        public IReadOnlyDictionary<ushort, int> Holders => holders;

        /// <summary>True while this instance owns the claim ids on the server's router.</summary>
        public bool IsAttachedToRouter => isAttachedToRouter;

        /// <summary>
        /// Claims the client-to-server claim ids on the server's router. Valid only when this is the sole
        /// session on that server; see the note on the type.
        /// </summary>
        public void AttachToRouter() {
            if (isAttachedToRouter) {
                return;
            }

            server.Router.Register<ClaimRequest>(ClaimMessageIds.ClaimRequest, HandleClaimRequest);
            server.Router.Register<ClaimRelease>(ClaimMessageIds.ClaimRelease, HandleClaimRelease);
            isAttachedToRouter = true;
        }

        /// <summary>Releases the ids claimed by <see cref="AttachToRouter"/>.</summary>
        public void DetachFromRouter() {
            if (!isAttachedToRouter) {
                return;
            }

            server.Router.Unregister(ClaimMessageIds.ClaimRequest);
            server.Router.Unregister(ClaimMessageIds.ClaimRelease);
            isAttachedToRouter = false;
        }

        /// <summary>
        /// Gives a slot to a peer if nobody else holds it.
        /// </summary>
        /// <remarks>
        /// Re-claiming a slot you already hold succeeds and broadcasts nothing: a client that spams the
        /// request while standing at a lever must not flood the session with verdicts that say what it
        /// already said. The membership check lives on the message handlers rather than here, so a host
        /// can seat a peer in a slot as part of its own bookkeeping — assigning a driver on match start,
        /// say — without the peer having asked.
        /// </remarks>
        /// <returns>True if the peer holds the slot when this returns.</returns>
        public bool TryClaim(ushort slot, PeerHandle peer) {
            if (!peer.IsValid) {
                return false;
            }

            if (holders.TryGetValue(slot, out int holder)) {
                return holder == peer.Id;
            }

            holders[slot] = peer.Id;
            PublishClaimChanged(slot, peer.Id);
            return true;
        }

        /// <summary>
        /// Frees a slot, but only for the peer that holds it.
        /// </summary>
        /// <returns>True if the slot was held by this peer and is now free.</returns>
        public bool Release(ushort slot, PeerHandle peer) {
            if (!holders.TryGetValue(slot, out int holder) || holder != peer.Id) {
                return false;
            }

            holders.Remove(slot);
            PublishClaimChanged(slot, FreeHolderPeerId);
            return true;
        }

        /// <summary>
        /// Frees every slot a peer holds — the disconnect and leave path.
        /// </summary>
        /// <remarks>
        /// A slot left held by a peer that is gone is unrecoverable without this: nobody else may release
        /// it, and the holder can never ask again. Called on both a transport drop and a graceful leave,
        /// because either may happen without the other.
        /// </remarks>
        public void ReleaseAllHeldBy(PeerHandle peer) {
            // The slots are collected before any of them is freed: the dictionary cannot be written while
            // it is being walked, and a listener on OnClaimChanged is free to claim or release from inside
            // the loop, which a reused scratch list would not survive.
            List<ushort> released = SlotsHeldBy(peer);

            for (int slotIndex = 0; slotIndex < released.Count; slotIndex++) {
                ushort slot = released[slotIndex];
                holders.Remove(slot);
                PublishClaimChanged(slot, FreeHolderPeerId);
            }
        }

        /// <summary>Peer id holding a slot, or -1 when it is free.</summary>
        public int Holder(ushort slot) {
            return holders.TryGetValue(slot, out int holder) ? holder : FreeHolderPeerId;
        }

        /// <summary>True while somebody holds the slot.</summary>
        public bool IsHeld(ushort slot) {
            return holders.ContainsKey(slot);
        }

        /// <summary>
        /// Sends one peer the current holder of every held slot — the join, rejoin and desync-repair path.
        /// </summary>
        /// <remarks>
        /// Free slots are not sent. A client starts with every slot free, and a rejoining one clears its
        /// map before the keyframe arrives, so silence about a slot is already the right answer and
        /// naming every unheld slot would make the keyframe grow with the game's slot space instead of
        /// with what is actually in use.
        /// </remarks>
        public void SendKeyframeTo(PeerHandle peer) {
            if (!peer.IsValid) {
                return;
            }

            foreach (KeyValuePair<ushort, int> held in holders) {
                var message = new ClaimChanged(held.Key, held.Value);
                server.Send(peer, ClaimMessageIds.ClaimChanged, in message, DeliveryClass.ReliableOrdered);
            }
        }

        /// <summary>
        /// Takes a peer's request for a slot. Public so a multi-session front desk can route to it after
        /// resolving which session the sender belongs to.
        /// </summary>
        public void HandleClaimRequest(in ClaimRequest message, PeerHandle sender) {
            if (!IsSessionPeer(sender)) {
                return;
            }

            TryClaim(message.Slot, sender);
        }

        /// <summary>
        /// Takes a peer's release of a slot. Public for the same reason as
        /// <see cref="HandleClaimRequest"/>.
        /// </summary>
        public void HandleClaimRelease(in ClaimRelease message, PeerHandle sender) {
            if (!IsSessionPeer(sender)) {
                return;
            }

            Release(message.Slot, sender);
        }

        /// <summary>Every slot a peer currently holds, as a list the caller may outlive the walk with.</summary>
        private List<ushort> SlotsHeldBy(PeerHandle peer) {
            var slots = new List<ushort>();

            foreach (KeyValuePair<ushort, int> held in holders) {
                if (held.Value == peer.Id) {
                    slots.Add(held.Key);
                }
            }

            return slots;
        }

        /// <summary>Tells the session about a transition, then whoever is watching this registry.</summary>
        private void PublishClaimChanged(ushort slot, int holderPeerId) {
            var message = new ClaimChanged(slot, holderPeerId);
            server.SendToMany(Peers, ClaimMessageIds.ClaimChanged, in message, DeliveryClass.ReliableOrdered);
            OnClaimChanged?.Invoke(slot, holderPeerId);
        }

        /// <summary>
        /// Whether a sender is one of this session's members. A connection may be authenticated, attached
        /// to a different session on the same server, or already retired from this one, and none of those
        /// may move this session's slots.
        /// </summary>
        private bool IsSessionPeer(PeerHandle sender) {
            IReadOnlyList<PeerHandle> peers = Peers;

            for (int peerIndex = 0; peerIndex < peers.Count; peerIndex++) {
                if (peers[peerIndex] == sender) {
                    return true;
                }
            }

            return false;
        }
    }
}
