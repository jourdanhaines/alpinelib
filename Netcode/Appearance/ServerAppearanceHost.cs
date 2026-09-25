using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// One session's player outfits: the authoritative record per member, the channel it is published
    /// on, and the rules a member's request to change theirs is held to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed by <see cref="AppearanceSubjectKey.FromPeerId"/>, so a client finds the body an outfit
    /// belongs to by the pawn's owner peer id without a separate table. Nothing is published for a
    /// member until they ask; a client shows its model's defaults until then.
    /// </para>
    /// <para>
    /// An accepted request is queued and adopted on the next tick rather than set where it lands. A
    /// state channel drops a record whose tick the client already holds, so two changes inside one
    /// tick would publish the first and lose the second; queueing makes the last request win.
    /// </para>
    /// <para>
    /// Engine-free and logger-free: a refusal is raised through <see cref="RequestRefused"/> for the
    /// game to report however it reports things.
    /// </para>
    /// </remarks>
    public sealed class ServerAppearanceHost {
        private readonly ServerStateChannel<AppearanceOutfitMessage> _channel;
        private readonly IAppearanceCatalog _catalog;
        private readonly Func<PeerHandle, ushort> _expectedModelOf;
        private readonly Dictionary<ushort, AppearanceOutfit> _outfits = new Dictionary<ushort, AppearanceOutfit>();
        private readonly Dictionary<ushort, uint> _keyTicks = new Dictionary<ushort, uint>();
        private readonly Dictionary<ushort, AppearanceOutfit> _pending = new Dictionary<ushort, AppearanceOutfit>();
        private readonly List<ushort> _adopted = new List<ushort>();

        /// <summary>Builds the host and the channel outfits travel on.</summary>
        /// <param name="server">The socket the channel publishes through, and the tick counter it stamps with.</param>
        /// <param name="peerSource">The session's member list, read fresh on every send.</param>
        /// <param name="config">Supplies the snapshot cadence, so the channel and the session agree.</param>
        /// <param name="messageId">The game-band id outfit records travel under.</param>
        /// <param name="catalog">The exported catalog requests are validated against.</param>
        /// <param name="expectedModelOf">
        /// The character model a peer spawned as, or 0 when the peer is not a member of this session.
        /// </param>
        /// <exception cref="ArgumentException">The id is one the library already speaks.</exception>
        public ServerAppearanceHost(
            NetServer server,
            Func<IReadOnlyList<PeerHandle>> peerSource,
            NetConfig config,
            ushort messageId,
            IAppearanceCatalog catalog,
            Func<PeerHandle, ushort> expectedModelOf) {
            if (server == null) {
                throw new ArgumentNullException(nameof(server));
            }

            MessageIdBudget.GuardGameMessageId(messageId, nameof(messageId));

            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _expectedModelOf = expectedModelOf ?? throw new ArgumentNullException(nameof(expectedModelOf));
            _channel = new ServerStateChannel<AppearanceOutfitMessage>(server, peerSource, messageId, config);
        }

        /// <summary>Raised with the sender, the outfit asked for and the reason whenever a request is refused.</summary>
        public event Action<PeerHandle, AppearanceOutfit, string> RequestRefused;

        /// <summary>Raised with the subject key and server tick when a member's outfit is adopted and differs.</summary>
        public event Action<ushort, uint> OutfitChanged;

        /// <summary>The channel player outfits travel on.</summary>
        public ServerStateChannel<AppearanceOutfitMessage> Channel => _channel;

        /// <summary>The outfit a member currently wears, or false when they never set one.</summary>
        public bool TryGetOutfit(ushort key, out AppearanceOutfit outfit) {
            return _outfits.TryGetValue(key, out outfit);
        }

        /// <summary>
        /// Takes a member's request to change their own outfit, queueing it for the next tick when the
        /// sender is a member and the outfit passes the rules for the model they spawned as.
        /// </summary>
        /// <returns>True when the request was accepted.</returns>
        public bool HandleRequest(PeerHandle sender, in AppearanceOutfitMessage message) {
            AppearanceOutfit outfit = message.Outfit;
            string refusal = RefusalFor(sender, in outfit);

            if (refusal != null) {
                RequestRefused?.Invoke(sender, outfit, refusal);
                return false;
            }

            _pending[AppearanceSubjectKey.FromPeerId(sender.Id)] = outfit;
            return true;
        }

        /// <summary>Adopts this tick's accepted requests, then pumps the channel.</summary>
        public void Tick(uint serverTick, float deltaSeconds) {
            AdoptPending(serverTick);
            _channel.Tick(serverTick, deltaSeconds);
        }

        /// <summary>Sends an arrival every member's outfit at once.</summary>
        public void SendKeyframeTo(PeerHandle peer) {
            _channel.SendKeyframeTo(peer);
        }

        /// <summary>Drops a departed member's outfit and any request still queued for them.</summary>
        /// <remarks>
        /// The key's last tick is kept: a peer id is recycled by the transport, and the next member to
        /// hold it must publish under a tick the clients have not yet seen for that key.
        /// </remarks>
        public void ForgetPeer(PeerHandle peer) {
            if (!peer.IsValid) {
                return;
            }

            ushort key = AppearanceSubjectKey.FromPeerId(peer.Id);
            _pending.Remove(key);
            _outfits.Remove(key);
            _channel.Remove(key);
        }

        private string RefusalFor(PeerHandle sender, in AppearanceOutfit outfit) {
            if (!sender.IsValid) {
                return "the sender is not a connected peer";
            }

            ushort expectedModelId = _expectedModelOf(sender);

            if (expectedModelId == 0) {
                return "the sender is not a member of this session";
            }

            if (!AppearanceRules.ValidateForModel(in outfit, expectedModelId, _catalog, out string error)) {
                return error;
            }

            return null;
        }

        /// <remarks>
        /// A request for a key already set this tick waits for the next one, so its record carries a
        /// tick the client has not yet seen for that key.
        /// </remarks>
        private void AdoptPending(uint serverTick) {
            if (_pending.Count == 0) {
                return;
            }

            _adopted.Clear();

            foreach (KeyValuePair<ushort, AppearanceOutfit> request in _pending) {
                if (_keyTicks.TryGetValue(request.Key, out uint lastTick) && serverTick <= lastTick) {
                    continue;
                }

                AdoptRequest(request.Key, request.Value, serverTick);
            }

            for (int adoptedIndex = 0; adoptedIndex < _adopted.Count; adoptedIndex++) {
                _pending.Remove(_adopted[adoptedIndex]);
            }
        }

        private void AdoptRequest(ushort key, AppearanceOutfit outfit, uint serverTick) {
            bool hadOutfit = _outfits.TryGetValue(key, out AppearanceOutfit previous);

            _outfits[key] = outfit;
            _keyTicks[key] = serverTick;
            _channel.Set(key, new AppearanceOutfitMessage(outfit), serverTick);
            _adopted.Add(key);

            if (hadOutfit && previous == outfit) {
                return;
            }

            OutfitChanged?.Invoke(key, serverTick);
        }
    }
}
