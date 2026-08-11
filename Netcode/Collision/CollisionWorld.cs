using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// A loaded scene's geometry, made queryable: broad phase over the statics, pure pose evaluation for
    /// the movers, and the two questions the motor asks — what am I touching, and what am I standing on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One world per scene, built once and then read-only. Both ends of the wire hold their own instance
    /// built from the identical exported bytes, which is what lets the shared motor run the same step in
    /// the server's tick loop and in the client's prediction replay.
    /// </para>
    /// <para>
    /// The tick interval is baked in at construction because mover poses are functions of the tick, and a
    /// world whose interval disagreed with the session's would put every platform somewhere slightly
    /// wrong. It is a property of the loaded world, not an argument callers can get wrong per call.
    /// </para>
    /// <para>
    /// <see cref="CollectContacts"/> reports static shapes first in ascending index order, then movers in
    /// ascending index order. That ordering is part of the determinism contract: depenetration is
    /// sequential and order-dependent, so a corner resolved statics-first on one end and movers-first on
    /// the other would settle a few micrometres apart and correct forever.
    /// </para>
    /// <para>
    /// <b>Determinism rules this file obeys, and any edit must keep.</b> Single-precision floats only,
    /// never <see cref="double"/>. A fixed operation order with no reassociation, which is why the mover
    /// delta subtracts components by hand rather than leaning on a vector operator whose lowering differs
    /// between runtimes. Only <c>+ - * /</c> and <see cref="MathF.Sqrt"/>/<see cref="MathF.Min"/>/
    /// <see cref="MathF.Max"/>/<see cref="MathF.Abs"/> on the position path; no trigonometry. Shapes are
    /// visited in ascending index order, statics before movers, and the best-wins comparisons below are
    /// strict, so the earliest candidate keeps a tie on both ends.
    /// </para>
    /// <para>
    /// Nothing here allocates. The candidate list and the contact list are both caller- or stack-owned
    /// spans, because these are the two calls the motor makes several times per substep of every tick of
    /// every pawn, and a per-query array would put the collision system in the server's GC profile.
    /// </para>
    /// </remarks>
    public sealed class CollisionWorld {
        /// <summary>
        /// Most contacts the motor will consider in one substep. Scratch spans are sized to this so the
        /// step path never allocates; a capsule genuinely touching seventeen things is wedged, and losing
        /// the seventeenth changes nothing it can feel.
        /// </summary>
        public const int MaxContacts = 16;

        /// <summary>
        /// Most broad-phase candidates one query considers. Larger than <see cref="MaxContacts"/> because
        /// candidates are shapes whose cells the capsule touches, most of which the narrow phase rejects;
        /// when a query overflows it is the highest shape indices that are dropped, identically on both
        /// ends of the wire.
        /// </summary>
        public const int MaxCandidates = 64;

        /// <summary>
        /// Tick interval a flat fallback world is built with. Only movers care, and a flat world has
        /// none — it exists so <see cref="Flat"/> can stay a one-argument call.
        /// </summary>
        public const float DefaultTickIntervalSeconds = 1f / 30f;

        private readonly CollisionGrid grid;

        /// <summary>Builds a queryable world from exported geometry.</summary>
        /// <param name="geometry">The scene's shapes and movers. Held, never mutated.</param>
        /// <param name="tickIntervalSeconds">The session's fixed tick length, which mover poses are phrased in.</param>
        public CollisionWorld(SceneGeometry geometry, float tickIntervalSeconds) {
            if (geometry == null) {
                throw new ArgumentNullException(nameof(geometry));
            }

            Geometry = geometry;
            TickIntervalSeconds = tickIntervalSeconds;
            grid = new CollisionGrid(geometry.StaticShapes);
            Movers = geometry.Movers;
        }

        /// <summary>
        /// The world a session falls back to when its scene has no exported geometry: one infinite floor,
        /// no walls, no movers. Warned about at load rather than treated as normal — it is the old flat
        /// plane, and pawns walk through everything on it.
        /// </summary>
        public static CollisionWorld Flat(float groundHeight = 0f) {
            var shapes = new[] { CollisionShape.MakePlane(groundHeight) };
            var geometry = new SceneGeometry(string.Empty, 0u, shapes, Array.Empty<MoverDefinition>());
            return new CollisionWorld(geometry, DefaultTickIntervalSeconds);
        }

        /// <summary>The geometry this world was built from.</summary>
        public SceneGeometry Geometry { get; }

        /// <summary>Fixed tick length mover poses are evaluated against, in seconds.</summary>
        public float TickIntervalSeconds { get; }

        /// <summary>The scene's movers, in export order. The index is the mover index everything else uses.</summary>
        public IReadOnlyList<MoverDefinition> Movers { get; }

        /// <summary>Broad-phase index over the static shapes.</summary>
        public CollisionGrid Grid => grid;

        /// <summary>Where a mover sits at a given tick. Pure — see <see cref="MoverPath"/>.</summary>
        /// <remarks>
        /// An index outside the mover list answers with the origin rather than throwing. Callers reach
        /// this from a support hit's <c>MoverIndex</c>, which is <c>-1</c> whenever the surface was static,
        /// and a sim tick is a bad place to discover that by way of an exception.
        /// </remarks>
        public Vector3 EvaluateMoverPosition(int moverIndex, uint tick) {
            if (moverIndex < 0 || moverIndex >= Movers.Count) {
                return Vector3.Zero;
            }

            MoverDefinition mover = Movers[moverIndex];
            if (mover?.Path == null) {
                return Vector3.Zero;
            }

            return mover.Path.EvaluatePosition(tick, TickIntervalSeconds);
        }

        /// <summary>
        /// How far a mover travelled between the previous tick and this one. This is the whole of the
        /// rider rule: a pawn standing on mover <c>m</c> adds this to its position before it moves, so it
        /// is carried without the motor keeping any state about what it is standing on.
        /// </summary>
        /// <remarks>
        /// Tick zero has no predecessor — subtracting one would wrap to <see cref="uint.MaxValue"/> and
        /// read a pose from an unrelated point in the cycle — so it reports no movement. Sessions start
        /// ticking at one, so this is a boundary condition rather than a case anybody rides through.
        /// </remarks>
        public Vector3 MoverDelta(int moverIndex, uint tick) {
            if (tick == 0u) {
                return Vector3.Zero;
            }

            Vector3 current = EvaluateMoverPosition(moverIndex, tick);
            Vector3 previous = EvaluateMoverPosition(moverIndex, tick - 1u);
            return new Vector3(current.X - previous.X, current.Y - previous.Y, current.Z - previous.Z);
        }

        /// <summary>
        /// Fills <paramref name="contacts"/> with every overlap between the capsule and the world at this
        /// tick: statics in ascending index order first, then movers in ascending index order.
        /// </summary>
        /// <returns>How many contacts were written, capped by the span's length.</returns>
        public int CollectContacts(in CapsulePose pose, uint tick, Span<CollisionContact> contacts) {
            if (contacts.Length == 0) {
                return 0;
            }

            Span<int> candidates = stackalloc int[MaxCandidates];
            int written = CollectStaticContacts(in pose, candidates, contacts);
            if (written >= contacts.Length) {
                return written;
            }

            return CollectMoverContacts(in pose, tick, contacts, written);
        }

        /// <summary>
        /// Finds the highest surface under a horizontal position within a vertical span, including mover
        /// surfaces at this tick.
        /// </summary>
        /// <remarks>
        /// "Highest" and not "highest walkable": this call has no slope limit and does not want one. It
        /// reports the surface and its normal, and the motor decides whether that normal is something it
        /// can stand on by comparing against <c>MathF.Cos(slopeLimit)</c> — keeping the profile out of the
        /// world means the same world answers the same query identically for every pawn on it.
        /// </remarks>
        /// <returns>True when something was found inside the span.</returns>
        public bool TryGetSupport(float x, float z, float probeTop, float probeBottom, uint tick, out SupportHit hit) {
            hit = default;
            Span<int> candidates = stackalloc int[MaxCandidates];
            int candidateCount = grid.Collect(x, x, z, z, candidates);
            bool found = AccumulateStaticSupport(candidates.Slice(0, candidateCount), x, z, probeTop, probeBottom, ref hit);
            return AccumulateMoverSupport(x, z, probeTop, probeBottom, tick, found, ref hit);
        }

        /// <summary>Tests the broad-phase candidates and writes the overlaps, ascending.</summary>
        private int CollectStaticContacts(in CapsulePose pose, Span<int> candidates, Span<CollisionContact> contacts) {
            float minX = pose.FootPosition.X - pose.Radius;
            float maxX = pose.FootPosition.X + pose.Radius;
            float minZ = pose.FootPosition.Z - pose.Radius;
            float maxZ = pose.FootPosition.Z + pose.Radius;
            int candidateCount = grid.Collect(minX, maxX, minZ, maxZ, candidates);

            CollisionShape[] statics = Geometry.StaticShapes;
            int written = 0;
            for (int index = 0; index < candidateCount; index++) {
                int shapeIndex = candidates[index];
                if (!CollisionResolver.TryGetContact(in pose, in statics[shapeIndex], out CollisionContact contact)) {
                    continue;
                }

                contacts[written] = new CollisionContact(contact.Normal, contact.Depth, shapeIndex);
                written++;
                if (written >= contacts.Length) {
                    return written;
                }
            }

            return written;
        }

        /// <summary>
        /// Tests every mover, in index order, against the capsule. There is no broad phase here on
        /// purpose: a scene has a handful of movers, their world shapes only exist once this tick's pose
        /// has been evaluated, and bucketing something that moves every tick costs more than testing it.
        /// </summary>
        private int CollectMoverContacts(in CapsulePose pose, uint tick, Span<CollisionContact> contacts, int written) {
            for (int moverIndex = 0; moverIndex < Movers.Count; moverIndex++) {
                CollisionShape worldShape = MoverWorldShape(moverIndex, tick);
                if (!CollisionResolver.TryGetContact(in pose, in worldShape, out CollisionContact contact)) {
                    continue;
                }

                contacts[written] = new CollisionContact(contact.Normal, contact.Depth, 0, true, moverIndex);
                written++;
                if (written >= contacts.Length) {
                    return written;
                }
            }

            return written;
        }

        /// <summary>Keeps the highest static surface found under the probe.</summary>
        private bool AccumulateStaticSupport(
            Span<int> candidates,
            float x,
            float z,
            float probeTop,
            float probeBottom,
            ref SupportHit hit) {
            CollisionShape[] statics = Geometry.StaticShapes;
            bool found = false;
            for (int index = 0; index < candidates.Length; index++) {
                int shapeIndex = candidates[index];
                bool supported = CollisionResolver.TryGetSupport(
                    in statics[shapeIndex],
                    x,
                    z,
                    probeTop,
                    probeBottom,
                    out float height,
                    out Vector3 normal);
                if (!supported || (found && height <= hit.Height)) {
                    continue;
                }

                hit = new SupportHit(height, normal, shapeIndex);
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Keeps the highest mover surface found under the probe, but only if it beats whatever the
        /// statics offered. The comparison is strict, so a platform resting exactly on the floor leaves
        /// the pawn standing on the floor — the static hit came first and a tie does not displace it.
        /// </summary>
        private bool AccumulateMoverSupport(
            float x,
            float z,
            float probeTop,
            float probeBottom,
            uint tick,
            bool found,
            ref SupportHit hit) {
            for (int moverIndex = 0; moverIndex < Movers.Count; moverIndex++) {
                CollisionShape worldShape = MoverWorldShape(moverIndex, tick);
                bool supported = CollisionResolver.TryGetSupport(
                    in worldShape,
                    x,
                    z,
                    probeTop,
                    probeBottom,
                    out float height,
                    out Vector3 normal);
                if (!supported || (found && height <= hit.Height)) {
                    continue;
                }

                hit = new SupportHit(height, normal, 0, true, moverIndex);
                found = true;
            }

            return found;
        }

        /// <summary>One mover's authored shape, translated to where its path puts it this tick.</summary>
        private CollisionShape MoverWorldShape(int moverIndex, uint tick) {
            MoverDefinition mover = Movers[moverIndex];
            return mover.LocalShape.Translated(EvaluateMoverPosition(moverIndex, tick));
        }
    }
}
