using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Netcode.Sessions.Spawning {
    /// <summary>
    /// Hands out authored spawn points in turn, and rings the extras around a point once the list has
    /// been round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a scene with markers in it uses. Authoring a point per player is the honest thing to do, but
    /// a lobby cap is raised far more often than a scene is re-exported, so a list shorter than the cap
    /// has to mean "start over" rather than "the ninth player stands inside the first". The overflow ring
    /// is small on purpose: an arrival on a second lap belongs beside the point it shares, not on the
    /// other side of the platform.
    /// </para>
    /// <para>
    /// The authored height is a nominal plane, not the answer: every point is probed against the scene's
    /// collision the same way the ring placement probes the origin plane.
    /// </para>
    /// </remarks>
    public sealed class ListSpawnPlacement : ISpawnPlacement {
        /// <summary>How far from its point an overflow arrival stands, in metres.</summary>
        public const float OverflowRadiusMetres = 1.5f;

        /// <summary>Seats on the overflow ring around one point before they repeat.</summary>
        public const int OverflowSeats = 8;

        private readonly SpawnPoint[] _points;
        private readonly SpawnGroundProbe _probe;

        private int _nextIndex;

        /// <summary>Builds a placement over an authored list.</summary>
        /// <param name="points">The points, in the order arrivals take them. Never null or empty.</param>
        public ListSpawnPlacement(
            IReadOnlyList<SpawnPoint> points,
            float probeAboveMetres = SpawnGroundProbe.DefaultAboveMetres,
            float probeBelowMetres = SpawnGroundProbe.DefaultBelowMetres) {
            if (points == null) {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Count == 0) {
                throw new ArgumentException("A list placement needs at least one spawn point.", nameof(points));
            }

            _points = CopyPoints(points);
            _probe = new SpawnGroundProbe(probeAboveMetres, probeBelowMetres);
        }

        /// <summary>The authored points, in the order arrivals take them.</summary>
        public IReadOnlyList<SpawnPoint> Points => _points;

        /// <inheritdoc />
        public PawnState NextSpawnState(SessionMember member, bool isRejoin, CollisionWorld world, uint serverTick) {
            int index = _nextIndex % _points.Length;
            int lap = _nextIndex / _points.Length;
            _nextIndex++;

            SpawnPoint point = _points[index];
            Vector3 nominal = point.Position + ResolveOverflowOffset(lap);

            return _probe.BuildStandingState(world, nominal, point.YawDegrees, serverTick);
        }

        /// <summary>
        /// Where an arrival stands relative to its point. The first lap takes the point itself; later ones
        /// take a seat on a small ring around it, so a list shorter than the roster still keeps bodies
        /// apart.
        /// </summary>
        private static Vector3 ResolveOverflowOffset(int lap) {
            if (lap <= 0) {
                return Vector3.Zero;
            }

            float angleRadians = (lap - 1) % OverflowSeats * (2f * MathF.PI / OverflowSeats);
            return new Vector3(
                MathF.Cos(angleRadians) * OverflowRadiusMetres,
                0f,
                MathF.Sin(angleRadians) * OverflowRadiusMetres);
        }

        /// <summary>
        /// Takes a private copy of the authored list, because a placement outlives the config asset it was
        /// built from and a list edited underneath it would move players already standing on it.
        /// </summary>
        private static SpawnPoint[] CopyPoints(IReadOnlyList<SpawnPoint> points) {
            var copy = new SpawnPoint[points.Count];

            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) {
                copy[pointIndex] = points[pointIndex];
            }

            return copy;
        }
    }
}
