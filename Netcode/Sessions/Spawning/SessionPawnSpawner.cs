using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions.Spawning {
    /// <summary>
    /// Gives every member of a session a body while they are in it: a pawn on arrival, none once they
    /// have gone, and a keyframe whenever the session says somebody needs the world in full.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="SessionHost"/> is a roster and knows nothing about entities; a
    /// <see cref="ServerReplication"/> is a world and knows nothing about seats. This is the join
    /// between them, and it is the same join on a listen host as on a dedicated server, which is why it
    /// lives here rather than in either one's front desk.
    /// </para>
    /// <para>
    /// <b>Pawns are keyed by <see cref="PlayerId"/>, never by peer id.</b> A member is retired before
    /// <see cref="SessionHost.OnMemberLeft"/> is raised, and retiring clears the peer id, so a despawn
    /// that looked the entity up by owner would find nothing and quietly leave a body standing in the
    /// lobby. The player id is the one handle that still means something at that moment, and it is also
    /// the handle a rejoin arrives under.
    /// </para>
    /// </remarks>
    public sealed class SessionPawnSpawner : IDisposable {
        private readonly SessionHost _host;
        private readonly ServerReplication _replication;
        private readonly ushort _prefabId;
        private readonly AuthorityMode _authority;
        private readonly ISpawnPlacement _placement;
        private readonly Dictionary<PlayerId, uint> _pawnByPlayer = new Dictionary<PlayerId, uint>();

        private bool _isDisposed;

        /// <summary>Binds a session's roster to the world its members are simulated in.</summary>
        /// <param name="host">The session whose arrivals and departures drive the pawns.</param>
        /// <param name="replication">The world the pawns live in. Its collision is what placements probe.</param>
        /// <param name="prefabId">Which entry of the client's prefab registry a pawn is.</param>
        /// <param name="authority">Who simulates the pawn once it exists.</param>
        /// <param name="placement">Where the next arrival appears.</param>
        public SessionPawnSpawner(
            SessionHost host,
            ServerReplication replication,
            ushort prefabId,
            AuthorityMode authority,
            ISpawnPlacement placement) {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _replication = replication ?? throw new ArgumentNullException(nameof(replication));
            _placement = placement ?? throw new ArgumentNullException(nameof(placement));
            _prefabId = prefabId;
            _authority = authority;

            _host.OnMemberJoined += HandleMemberJoined;
            _host.OnMemberLeft += HandleMemberLeft;
            _host.OnMemberNeedsKeyframe += HandleMemberNeedsKeyframe;
        }

        /// <summary>A member's pawn was created. Raised after the spawn has gone out to the session.</summary>
        public event Action<SessionMember, NetEntity> OnPawnSpawned;

        /// <summary>Which entry of the prefab registry these pawns are.</summary>
        public ushort PrefabId => _prefabId;

        /// <summary>Who simulates the pawns this spawner creates.</summary>
        public AuthorityMode Authority => _authority;

        /// <summary>The pawn a player currently has, if they have one.</summary>
        public bool TryGetPawn(PlayerId player, out uint entityId) {
            return _pawnByPlayer.TryGetValue(player, out entityId);
        }

        /// <summary>
        /// Stops reacting to the session and forgets which pawn belonged to whom.
        /// </summary>
        /// <remarks>
        /// The pawns themselves are left alone. Disposal happens when the session is closing and the whole
        /// world is about to go with it, and despawning here would broadcast a departure for every member
        /// to clients that are being told the session has ended in the same breath.
        /// </remarks>
        public void Dispose() {
            if (_isDisposed) {
                return;
            }

            _isDisposed = true;

            _host.OnMemberJoined -= HandleMemberJoined;
            _host.OnMemberLeft -= HandleMemberLeft;
            _host.OnMemberNeedsKeyframe -= HandleMemberNeedsKeyframe;
            _pawnByPlayer.Clear();
        }

        /// <summary>
        /// Gives an arriving member a body. A rejoining player's pawn was despawned when their link
        /// dropped, so both paths spawn: the seat came back, the body did not.
        /// </summary>
        private void HandleMemberJoined(SessionMember member, bool isRejoin) {
            if (member == null) {
                return;
            }

            DespawnPawnFor(member.PlayerId);

            PawnState spawnState = _placement.NextSpawnState(member, isRejoin, _replication.World, _replication.CurrentTick);
            NetEntity pawn = _replication.SpawnEntity(_prefabId, member.PeerId, _authority, in spawnState);
            _pawnByPlayer[member.PlayerId] = pawn.Id;

            OnPawnSpawned?.Invoke(member, pawn);
        }

        /// <summary>
        /// Takes the body away with the seat, whether the member quit, was kicked or simply stopped
        /// answering.
        /// </summary>
        private void HandleMemberLeft(SessionMember member, LeaveReason reason) {
            if (member == null) {
                return;
            }

            DespawnPawnFor(member.PlayerId);
        }

        /// <summary>Sends the world in full to a member the session says needs it.</summary>
        private void HandleMemberNeedsKeyframe(SessionMember member) {
            if (member == null || member.PeerId == SessionMember.NoPeerId) {
                return;
            }

            _replication.SendKeyframeTo(new PeerHandle(member.PeerId));
        }

        private void DespawnPawnFor(PlayerId player) {
            if (!_pawnByPlayer.TryGetValue(player, out uint entityId)) {
                return;
            }

            _pawnByPlayer.Remove(player);
            _replication.DespawnEntity(entityId);
        }
    }
}
