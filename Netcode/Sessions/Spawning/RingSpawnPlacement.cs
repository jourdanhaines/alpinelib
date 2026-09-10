using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Netcode.Sessions.Spawning {
    /// <summary>
    /// Seats arrivals evenly around a ring centred on the origin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default placement, and the one a scene with no authored markers gets. It needs nothing from
    /// the scene but its floor, so a session can open before anybody has exported geometry for it and
    /// still put its members somewhere sensible rather than all on the same spot.
    /// </para>
    /// <para>
    /// Seats are handed out, never returned: the count is of arrivals ever, not of members present, so a
    /// lobby with churn puts arrival nine on top of whoever is standing at seat zero. A scene that cares
    /// authors markers and uses <see cref="ListSpawnPlacement"/>, which widens once before it starts over
    /// and so holds twice as many arrivals per marker.
    /// </para>
    /// </remarks>
    public sealed class RingSpawnPlacement : ISpawnPlacement {
        /// <summary>
        /// Radius of the ring arrivals are placed on. Wide enough that neighbouring seats of a default
        /// ring stand clear of one another; arrivals past a full lap share a seat outright.
        /// </summary>
        public const float DefaultRadiusMetres = 2f;

        /// <summary>Seats on the ring before positions repeat. Matches the usual lobby cap.</summary>
        public const int DefaultSeats = 8;

        private readonly float _radiusMetres;
        private readonly int _seats;
        private readonly SpawnGroundProbe _probe;

        private int _nextSeat;

        /// <summary>Builds a ring of the given size.</summary>
        /// <param name="radiusMetres">How far from the origin the seats sit.</param>
        /// <param name="seats">How many arrivals are seated before the ring starts over. Must be positive.</param>
        public RingSpawnPlacement(
            float radiusMetres = DefaultRadiusMetres,
            int seats = DefaultSeats,
            float probeAboveMetres = SpawnGroundProbe.DefaultAboveMetres,
            float probeBelowMetres = SpawnGroundProbe.DefaultBelowMetres) {
            if (seats <= 0) {
                throw new ArgumentOutOfRangeException(nameof(seats), seats, "A spawn ring needs at least one seat.");
            }

            _radiusMetres = radiusMetres;
            _seats = seats;
            _probe = new SpawnGroundProbe(probeAboveMetres, probeBelowMetres);
        }

        /// <summary>Radius of the ring, in metres.</summary>
        public float RadiusMetres => _radiusMetres;

        /// <summary>How many arrivals are seated before the ring starts over.</summary>
        public int Seats => _seats;

        /// <inheritdoc />
        public PawnState NextSpawnState(SessionMember member, bool isRejoin, CollisionWorld world, uint serverTick) {
            float angleRadians = _nextSeat * (2f * MathF.PI / _seats);

            // Wrapped on the increment rather than on read: a counter that only ever grew would run past
            // int.MaxValue in a session that never closes and start mirroring the ring.
            _nextSeat = (_nextSeat + 1) % _seats;

            float x = MathF.Cos(angleRadians) * _radiusMetres;
            float z = MathF.Sin(angleRadians) * _radiusMetres;

            return _probe.BuildStandingState(world, new Vector3(x, 0f, z), 0f, serverTick);
        }
    }
}
