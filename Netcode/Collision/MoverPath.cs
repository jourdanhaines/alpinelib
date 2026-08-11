using System;
using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// A mover's route through the world, evaluated as a pure function of the simulation tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole trick that makes moving platforms work in a predicted, rewound simulation.
    /// Nothing here integrates: a platform's pose at tick <c>N</c> is computed from <c>N</c> alone, so a
    /// client replaying six ticks of input during reconciliation gets the platform where the server had
    /// it on each of those six ticks, without having to record or replay the platform at all. A mover
    /// with accumulated state would make every rewind a fresh source of divergence.
    /// </para>
    /// <para>
    /// The cycle is measured in <b>ticks</b>, not seconds, and the tick counter is reduced modulo it. A
    /// path phrased in seconds would drift as the float tick counter grew; phrased in whole ticks it is
    /// exact forever, and <see cref="PhaseTicks"/> lets several movers share one path while sitting at
    /// different points along it.
    /// </para>
    /// <para>
    /// One traversal is rounded <i>up</i> to a whole number of ticks, which places the reflection point of
    /// a ping-pong exactly on the last waypoint: the distance travelled by then has met or passed the
    /// path's length and is clamped to its end. The cost is that the final tick of a traversal covers a
    /// slightly short step; the alternative — stretching the path to fit the tick grid — would make the
    /// authored speed a polite suggestion, which reads far worse when two platforms are meant to move
    /// together.
    /// </para>
    /// <para>
    /// <b>Determinism rules this file obeys, and any edit must keep.</b> Single-precision floats only,
    /// never <see cref="double"/>; integer arithmetic for the tick reduction so the cycle never rounds,
    /// and the phase is folded modulo the cycle <i>before</i> it is added so a tick counter near its
    /// ceiling cannot wrap into a different point on the path. A fixed operation order with no
    /// reassociation: segment lengths accumulate front to back once in the constructor and are read back
    /// as cumulative differences, never re-derived. Only <c>+ - * /</c> and
    /// <see cref="MathF.Sqrt"/>/<see cref="MathF.Min"/>/<see cref="MathF.Max"/>/<see cref="MathF.Abs"/>
    /// on the position path; no trigonometry, and no easing curve that would smuggle one in. The final
    /// interpolation is written out component by component in scalar float rather than through
    /// <see cref="Vector3.Lerp"/>, because a vectorised lerp is free to contract its multiply and add
    /// into one fused instruction on a machine that has one and not on a machine that does not.
    /// </para>
    /// </remarks>
    public sealed class MoverPath {
        /// <summary>
        /// Ceiling on one traversal's tick count. It exists so that doubling for a ping-pong cannot
        /// overflow, and so that a mover authored with an absurdly small speed parks somewhere finite
        /// instead of dividing by a cycle that wrapped to zero.
        /// </summary>
        private const uint MaxTraversalTicks = 1u << 30;

        /// <summary>Distance from the first waypoint to each waypoint, accumulated front to back once.</summary>
        private readonly float[] cumulativeDistances;

        /// <summary>Creates a path. The waypoint array is held, not copied — treat it as immutable.</summary>
        /// <param name="waypoints">At least two world positions, visited in order.</param>
        /// <param name="speed">Travel speed in metres per second. Must be positive.</param>
        /// <param name="loopMode">What happens at the end of the list.</param>
        /// <param name="phaseTicks">Offset into the cycle, so identical movers can run out of step.</param>
        public MoverPath(Vector3[] waypoints, float speed, MoverLoopMode loopMode, uint phaseTicks) {
            Waypoints = waypoints ?? Array.Empty<Vector3>();
            Speed = speed;
            LoopMode = loopMode;
            PhaseTicks = phaseTicks;
            cumulativeDistances = new float[Waypoints.Length];
            TotalLength = MeasureWaypoints(Waypoints, cumulativeDistances);
        }

        /// <summary>World positions the mover visits in order. Held, never mutated.</summary>
        public Vector3[] Waypoints { get; }

        /// <summary>Travel speed in metres per second.</summary>
        public float Speed { get; }

        /// <summary>What happens when the last waypoint is reached.</summary>
        public MoverLoopMode LoopMode { get; }

        /// <summary>Offset into the cycle, in ticks.</summary>
        public uint PhaseTicks { get; }

        /// <summary>
        /// Total length of the waypoint chain in metres, measured once at construction. Exposed because
        /// the exporter and its validators want to talk about a path's length without walking it again,
        /// and because a path whose length is zero is an authoring mistake worth surfacing.
        /// </summary>
        public float TotalLength { get; }

        /// <summary>
        /// Length of one complete cycle in whole ticks: the path's traversal for <see cref="MoverLoopMode.Loop"/>,
        /// twice that for <see cref="MoverLoopMode.PingPong"/>. Never zero, so callers may reduce modulo it
        /// unguarded.
        /// </summary>
        public uint CycleTicks(float tickIntervalSeconds) {
            uint traversalTicks = TraversalTicks(tickIntervalSeconds);
            if (LoopMode != MoverLoopMode.PingPong) {
                return traversalTicks;
            }

            return traversalTicks * 2u;
        }

        /// <summary>
        /// Where the mover is at a given simulation tick. Pure: the same tick always yields the same
        /// position, on both ends of the wire and however many times prediction replays it.
        /// </summary>
        public Vector3 EvaluatePosition(uint tick, float tickIntervalSeconds) {
            if (Waypoints.Length == 0) {
                return Vector3.Zero;
            }

            if (Waypoints.Length == 1) {
                return Waypoints[0];
            }

            uint cycleTicks = CycleTicks(tickIntervalSeconds);
            uint phasedTicks = PhasedTicks(tick, cycleTicks);
            uint forwardTicks = ForwardTicks(phasedTicks, cycleTicks);
            float seconds = forwardTicks * tickIntervalSeconds;
            float distance = seconds * Speed;
            return PositionAtDistance(distance);
        }

        /// <summary>
        /// Walks the waypoints front to back once, filling the cumulative distance table and returning the
        /// total. Called only from the constructor: this is the one place segment lengths are computed, so
        /// every later query reads back the same floats in the same order rather than re-deriving them.
        /// </summary>
        private static float MeasureWaypoints(Vector3[] waypoints, float[] cumulative) {
            float running = 0f;
            for (int waypointIndex = 1; waypointIndex < waypoints.Length; waypointIndex++) {
                Vector3 previous = waypoints[waypointIndex - 1];
                Vector3 current = waypoints[waypointIndex];
                float deltaX = current.X - previous.X;
                float deltaY = current.Y - previous.Y;
                float deltaZ = current.Z - previous.Z;
                float segmentLength = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
                running = running + segmentLength;
                cumulative[waypointIndex] = running;
            }

            return running;
        }

        /// <summary>
        /// Ticks taken to walk the path once, rounded up, clamped to something a cycle can survive. A
        /// degenerate path — no length, no speed, no tick interval — reports a single tick, which keeps
        /// every modulo downstream safe and parks the mover on its first waypoint.
        /// </summary>
        private uint TraversalTicks(float tickIntervalSeconds) {
            if (TotalLength <= 0f || Speed <= 0f || tickIntervalSeconds <= 0f) {
                return 1u;
            }

            float seconds = TotalLength / Speed;
            float exactTicks = seconds / tickIntervalSeconds;
            if (!(exactTicks < MaxTraversalTicks)) {
                return MaxTraversalTicks;
            }

            uint wholeTicks = (uint)exactTicks;
            if (wholeTicks < exactTicks) {
                wholeTicks = wholeTicks + 1u;
            }

            return wholeTicks == 0u ? 1u : wholeTicks;
        }

        /// <summary>
        /// The tick's position within the cycle, phase applied. Both terms are folded modulo the cycle
        /// before they are added, and the addition is widened, so a tick counter close to its ceiling
        /// cannot wrap around and land the platform somewhere else entirely.
        /// </summary>
        private uint PhasedTicks(uint tick, uint cycleTicks) {
            ulong sum = (ulong)(tick % cycleTicks) + (PhaseTicks % cycleTicks);
            return (uint)(sum % cycleTicks);
        }

        /// <summary>
        /// How far into a forward traversal the cycle position corresponds to. A loop is already forward;
        /// a ping-pong reflects its second half, so tick <c>half + k</c> lands exactly where <c>half − k</c>
        /// did — the return trip is the outbound trip read backwards, not a second path with its own
        /// rounding.
        /// </summary>
        private uint ForwardTicks(uint phasedTicks, uint cycleTicks) {
            if (LoopMode != MoverLoopMode.PingPong) {
                return phasedTicks;
            }

            uint traversalTicks = cycleTicks / 2u;
            if (phasedTicks <= traversalTicks) {
                return phasedTicks;
            }

            return cycleTicks - phasedTicks;
        }

        /// <summary>
        /// The point that many metres along the waypoint chain. Segments are searched in ascending index
        /// order — the same order they were measured in — and distances past either end clamp to the
        /// terminal waypoints rather than extrapolating.
        /// </summary>
        private Vector3 PositionAtDistance(float distance) {
            if (distance <= 0f) {
                return Waypoints[0];
            }

            if (distance >= TotalLength) {
                return Waypoints[Waypoints.Length - 1];
            }

            for (int segmentIndex = 0; segmentIndex + 1 < Waypoints.Length; segmentIndex++) {
                if (distance >= cumulativeDistances[segmentIndex + 1]) {
                    continue;
                }

                return InterpolateSegment(segmentIndex, distance - cumulativeDistances[segmentIndex]);
            }

            return Waypoints[Waypoints.Length - 1];
        }

        /// <summary>
        /// Linear interpolation along one segment, component by component in scalar float so no fused
        /// multiply-add can creep in and give two machines two answers.
        /// </summary>
        private Vector3 InterpolateSegment(int segmentIndex, float distanceIntoSegment) {
            Vector3 start = Waypoints[segmentIndex];
            Vector3 end = Waypoints[segmentIndex + 1];
            float segmentLength = cumulativeDistances[segmentIndex + 1] - cumulativeDistances[segmentIndex];
            if (segmentLength <= 0f) {
                return start;
            }

            float fraction = distanceIntoSegment / segmentLength;
            float x = start.X + (end.X - start.X) * fraction;
            float y = start.Y + (end.Y - start.Y) * fraction;
            float z = start.Z + (end.Z - start.Z) * fraction;
            return new Vector3(x, y, z);
        }
    }
}
