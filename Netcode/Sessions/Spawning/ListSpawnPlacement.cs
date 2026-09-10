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
    /// <b>How the overflow spreads out.</b> Arrival <i>n</i> beyond the authored list takes overflow seat
    /// <i>n</i>, and the seat alone fixes both the direction and the distance: the angle is seat
    /// <c>n % OverflowSeats</c> of an eight-seat ring turned half a seat off the axes, and the radius grows
    /// by <see cref="OverflowRadiusMetres"/> every revolution. Seeding from the arrival rather than the lap
    /// matters because a lap-only offset shifted every authored point by the same vector, so a row of
    /// markers a lap-radius apart overlapped itself; the half-seat turn keeps the ring off +X and +Z, which
    /// is where hand-authored rows live. Because the radius grows, positions never repeat — no arrival
    /// count puts two pawns on the same spot.
    /// </para>
    /// <para>
    /// The authored height is a nominal plane, not the answer: every point is probed against the scene's
    /// collision the same way the ring placement probes the origin plane. The probe reaches much further
    /// down than up, so a marker authored more than the probe's upward reach <i>below</i> the real floor
    /// keeps its nominal height and its pawn spawns inside the geometry. Authoring tools should warn on
    /// that rather than expecting the probe to rescue it.
    /// </para>
    /// </remarks>
    public sealed class ListSpawnPlacement : ISpawnPlacement {
        /// <summary>How far from its point the first revolution of overflow arrivals stands, in metres.</summary>
        public const float OverflowRadiusMetres = 1.5f;

        /// <summary>Seats on one revolution of the overflow ring, after which it widens rather than repeats.</summary>
        public const int OverflowSeats = 8;

        private readonly SpawnPoint[] _points;
        private readonly SpawnGroundProbe _probe;

        // Counted as a long so a session that never closes cannot wrap the ordinal into a negative index.
        private long _nextArrival;

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
            long arrival = _nextArrival;
            _nextArrival++;

            SpawnPoint point = _points[(int)(arrival % _points.Length)];

            // The first pass over the list takes the points verbatim, so the overflow seat only starts
            // counting once every authored point has been handed out.
            Vector3 nominal = point.Position + ResolveOverflowOffset(arrival - _points.Length);

            return _probe.BuildStandingState(world, nominal, point.YawDegrees, serverTick);
        }

        /// <summary>
        /// Where an overflow arrival stands relative to its point: a seat on a ring that turns half a seat
        /// off the axes and widens by <see cref="OverflowRadiusMetres"/> each revolution, so no two
        /// arrivals ever share a place however long the session runs.
        /// </summary>
        /// <param name="overflowSeat">The arrival's ordinal past the authored list. Negative on the first pass.</param>
        private static Vector3 ResolveOverflowOffset(long overflowSeat) {
            if (overflowSeat < 0L) {
                return Vector3.Zero;
            }

            long revolution = overflowSeat / OverflowSeats;
            long seatOnRing = overflowSeat % OverflowSeats;

            float angleRadians = (seatOnRing + 0.5f) * (2f * MathF.PI / OverflowSeats);
            float radiusMetres = OverflowRadiusMetres * (1f + revolution);

            return new Vector3(
                MathF.Cos(angleRadians) * radiusMetres,
                0f,
                MathF.Sin(angleRadians) * radiusMetres);
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
