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
    /// <b>How the overflow spreads out.</b> A marker's <i>k</i>th trip round the list takes overflow seat
    /// <i>k</i> on that marker's own ring — counted per marker, not per arrival, because the arrival
    /// ordinal jumps by the list length between two arrivals at one marker and would stand the seat still
    /// whenever the two share a factor. The seat fixes both the direction and the distance: the angle is
    /// one of <see cref="OverflowSeats"/> directions on a ring turned half a seat off +X and +Z, where
    /// hand-authored rows live, and each full revolution steps the radius out by
    /// <see cref="OverflowRadiusMetres"/>. The widening stops after <see cref="OverflowRevolutions"/>, so a
    /// marker holds <c>OverflowSeats * OverflowRevolutions</c> overflow places, whatever the list length,
    /// and then starts over — a marker's seventeenth overflow arrival stands where its first did. It
    /// repeats on purpose: the probe answers about the plane the marker was authored on, so a seat widened
    /// past the edge of that platform is dropped onto whatever is twenty metres below, which is worse than
    /// a shared spot.
    /// A marker therefore wants <c>OverflowRadiusMetres * OverflowRevolutions</c> of surface around it.
    /// </para>
    /// <para>
    /// <b>Distinct seats, not spaced ones.</b> Each marker turns its ring two seats further round than the
    /// marker before it, so two arrivals in a row land a quarter turn apart instead of an eighth and a row
    /// of markers fans its overflow out rather than leaning it all the same way. Nothing here reads the
    /// authored spacing, though, so this is not a promise that two pawns never overlap: overflow arrivals
    /// belonging to different markers can still stand closer together than a pawn is wide. A game that
    /// needs a guaranteed gap authors more points — the list is handed out in order and does not overflow
    /// at all while there are unused points left.
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

        /// <summary>Seats on one revolution of the overflow ring, after which it widens by a radius.</summary>
        public const int OverflowSeats = 8;

        /// <summary>
        /// How far the ring widens before its seats repeat. Two revolutions reach three metres, which is
        /// as far as a seat can go and still be on the platform a marker was authored on.
        /// </summary>
        public const int OverflowRevolutions = 2;

        private readonly SpawnPoint[] _points;
        private readonly SpawnGroundProbe _probe;

        // Counted as a long so no session that can exist wraps the ordinal into a negative index: the wrap
        // itself would throw, but it is nine quintillion arrivals away.
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

            int pointIndex = (int)(arrival % _points.Length);
            SpawnPoint point = _points[pointIndex];

            // The first pass over the list takes the points verbatim, so the overflow seat only starts
            // counting once every authored point has been handed out.
            Vector3 nominal = point.Position + ResolveOverflowOffset(arrival - _points.Length, pointIndex);

            return _probe.BuildStandingState(world, nominal, point.YawDegrees, serverTick);
        }

        /// <summary>
        /// Where an overflow arrival stands relative to its point: a seat on a ring turned half a seat off
        /// the axes, widening by <see cref="OverflowRadiusMetres"/> each revolution and starting over after
        /// <see cref="OverflowRevolutions"/> of them.
        /// </summary>
        /// <param name="overflowOrdinal">The arrival's ordinal past the authored list. Negative on the first pass.</param>
        /// <param name="pointIndex">Which marker the arrival is ringing. It turns the ring off its neighbours'.</param>
        private Vector3 ResolveOverflowOffset(long overflowOrdinal, int pointIndex) {
            if (overflowOrdinal < 0L) {
                return Vector3.Zero;
            }

            // Trips round the list, not arrivals overall: the ordinal advances by the list length between
            // two arrivals at one marker, which stands the seat still whenever the two share a factor.
            long roundAtMarker = overflowOrdinal / _points.Length;

            // The revolution wraps rather than growing without end: a seat further out than the surface the
            // marker was authored on is worse than a shared one.
            long revolution = roundAtMarker / OverflowSeats % OverflowRevolutions;

            // Two seats of turn per marker, so neighbouring markers in a row send their overflow arrivals a
            // quarter turn apart instead of leaning them all the same way.
            long seatOnRing = (roundAtMarker + 2L * pointIndex) % OverflowSeats;

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
