using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;

namespace AlpineLib.Server.Tests {
    /// <summary>Seats every arrival at one fixed spot and records each ask, rejoin flag included.</summary>
    internal sealed class RecordingSpawnPlacement : ISpawnPlacement {
        private readonly Vector3 _position;

        public RecordingSpawnPlacement(Vector3 position) {
            _position = position;
        }

        /// <summary>The rejoin flag of every ask, in order.</summary>
        public List<bool> RejoinFlags { get; } = new List<bool>();

        /// <inheritdoc />
        public PawnState NextSpawnState(SessionMember member, bool isRejoin, CollisionWorld world, uint serverTick) {
            RejoinFlags.Add(isRejoin);
            return new PawnState(_position, 0f, Vector3.Zero, 0);
        }
    }
}
