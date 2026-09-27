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
    /// One world per scene. Both ends of the wire hold their own instance built from the identical
    /// exported bytes, which is what lets the shared motor run the same step in the server's tick loop
    /// and in the client's prediction replay.
    /// </para>
    /// <para>
    /// <b>Static shapes live in keyed groups.</b> The scene's exported statics are group 0; a streamed
    /// world adds and removes further groups (one per cell, say) with <see cref="AddGroup"/> and
    /// <see cref="RemoveGroup"/>, and <see cref="TranslateAll"/> shifts everything for a floating origin.
    /// Queries see the groups flattened in ascending key order, each group's shapes in their own order,
    /// so the same set of groups yields the same shape indices however it was assembled. The broad phase
    /// is rebuilt on the first query after a change, never per query.
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
    /// Queries do not allocate, except the one rebuild that follows a group change. The candidate list and the contact list are both caller- or stack-owned
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

        /// <summary>Key the scene's exported static shapes are held under.</summary>
        public const ulong SceneGroupKey = 0ul;

        private readonly SortedDictionary<ulong, CollisionGroup> groups = new SortedDictionary<ulong, CollisionGroup>();

        private CollisionGrid grid;
        private CollisionShape[] statics = Array.Empty<CollisionShape>();
        private ulong[] groupKeys = Array.Empty<ulong>();
        private int[] groupStarts = Array.Empty<int>();
        private Vector3 moverOffset;
        private bool isDirty;

        /// <summary>Builds a queryable world from exported geometry.</summary>
        /// <param name="geometry">The scene's shapes and movers. Held, never mutated.</param>
        /// <param name="tickIntervalSeconds">The session's fixed tick length, which mover poses are phrased in.</param>
        public CollisionWorld(SceneGeometry geometry, float tickIntervalSeconds) {
            if (geometry == null) {
                throw new ArgumentNullException(nameof(geometry));
            }

            Geometry = geometry;
            TickIntervalSeconds = tickIntervalSeconds;
            Movers = geometry.Movers;
            groups.Add(SceneGroupKey, new CollisionGroup(SceneGroupKey, geometry.StaticShapes ?? Array.Empty<CollisionShape>(), Vector3.Zero));
            isDirty = true;
            EnsureBuilt();
        }

        /// <summary>
        /// The world a session falls back to when its scene has no exported geometry: one infinite floor,
        /// no walls, no movers. Warned about at load rather than treated as normal — it is the old flat
        /// plane, and pawns walk through everything on it.
        /// </summary>
        public static CollisionWorld Flat(float groundHeight = 0f) {
            var shapes = new[] { CollisionShape.MakePlane(groundHeight) };
            var geometry = new SceneGeometry(string.Empty, 0u, shapes, Array.Empty<MoverDefinition>());
            return new CollisionWorld(geometry, DefaultTickIntervalSeconds) { IsFallback = true };
        }

        /// <summary>
        /// Whether this is the <see cref="Flat"/> stand-in rather than a scene's exported geometry. Its
        /// floor is a guess, so nothing authored against the real scene should be corrected to it.
        /// </summary>
        public bool IsFallback { get; private set; }

        /// <summary>
        /// The geometry this world was built from. Its static shapes are group <see cref="SceneGroupKey"/>
        /// as exported, before any <see cref="TranslateAll"/>; query results index the flattened view
        /// behind <see cref="GetStaticShape"/> instead.
        /// </summary>
        public SceneGeometry Geometry { get; }

        /// <summary>Fixed tick length mover poses are evaluated against, in seconds.</summary>
        public float TickIntervalSeconds { get; }

        /// <summary>The scene's movers, in export order. The index is the mover index everything else uses.</summary>
        public IReadOnlyList<MoverDefinition> Movers { get; }

        /// <summary>Broad-phase index over the static shapes, rebuilt first if a group changed.</summary>
        public CollisionGrid Grid {
            get {
                EnsureBuilt();
                return grid;
            }
        }

        /// <summary>How many static groups the world holds, the scene's own included while present.</summary>
        public int GroupCount => groups.Count;

        /// <summary>Bumped on every group change or translation, so a cache keyed on it knows to refresh.</summary>
        public int Revision { get; private set; }

        /// <summary>How many static shapes all groups hold together.</summary>
        public int StaticShapeCount {
            get {
                EnsureBuilt();
                return statics.Length;
            }
        }

        /// <summary>
        /// One static shape in world space, by the index contacts and support hits report: groups in
        /// ascending key order, each group's shapes in their given order.
        /// </summary>
        public CollisionShape GetStaticShape(int shapeIndex) {
            EnsureBuilt();
            return statics[shapeIndex];
        }

        /// <summary>True when a group is held under this key.</summary>
        public bool HasGroup(ulong key) {
            return groups.ContainsKey(key);
        }

        /// <summary>
        /// Adds a batch of static shapes, given relative to <paramref name="offset"/> in the current world
        /// frame. The array is copied.
        /// </summary>
        /// <remarks>
        /// A world resolved from a shared <see cref="SceneGeometryLibrary"/> is shared by every session on
        /// the process; build a private one from its <see cref="Geometry"/> before adding to it.
        /// </remarks>
        /// <exception cref="ArgumentException">A group is already held under <paramref name="key"/>.</exception>
        public void AddGroup(ulong key, CollisionShape[] shapes, Vector3 offset) {
            if (shapes == null) {
                throw new ArgumentNullException(nameof(shapes));
            }

            if (groups.ContainsKey(key)) {
                throw new ArgumentException("A collision group is already held under key " + key.ToString() + ".", nameof(key));
            }

            groups.Add(key, new CollisionGroup(key, (CollisionShape[])shapes.Clone(), offset));
            MarkChanged();
        }

        /// <summary>Drops a group. False when no group was held under the key.</summary>
        public bool RemoveGroup(ulong key) {
            if (!groups.Remove(key)) {
                return false;
            }

            MarkChanged();
            return true;
        }

        /// <summary>
        /// Moves every group and every mover by <paramref name="delta"/>, as a floating origin rebase does.
        /// Groups added afterwards are placed in the new frame.
        /// </summary>
        public void TranslateAll(Vector3 delta) {
            foreach (CollisionGroup group in groups.Values) {
                Vector3 offset = group.Offset;
                group.Offset = new Vector3(offset.X + delta.X, offset.Y + delta.Y, offset.Z + delta.Z);
            }

            moverOffset = new Vector3(moverOffset.X + delta.X, moverOffset.Y + delta.Y, moverOffset.Z + delta.Z);
            MarkChanged();
        }

        /// <summary>
        /// Which group a static shape index belongs to, and its index inside that group.
        /// </summary>
        /// <returns>False when the index is outside <see cref="StaticShapeCount"/>.</returns>
        public bool TryResolveShape(int shapeIndex, out ulong groupKey, out int localIndex) {
            EnsureBuilt();
            groupKey = 0ul;
            localIndex = -1;

            if (shapeIndex < 0 || shapeIndex >= statics.Length) {
                return false;
            }

            int groupIndex = FindGroupIndex(shapeIndex);
            groupKey = groupKeys[groupIndex];
            localIndex = shapeIndex - groupStarts[groupIndex];
            return true;
        }

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

            Vector3 pathPosition = mover.Path.EvaluatePosition(tick, TickIntervalSeconds);
            return new Vector3(pathPosition.X + moverOffset.X, pathPosition.Y + moverOffset.Y, pathPosition.Z + moverOffset.Z);
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

            EnsureBuilt();
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
            EnsureBuilt();
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

        private void MarkChanged() {
            isDirty = true;
            Revision++;
        }

        /// <summary>Re-flattens the groups and rebuckets them, once per change.</summary>
        private void EnsureBuilt() {
            if (!isDirty) {
                return;
            }

            int total = 0;
            foreach (CollisionGroup group in groups.Values) {
                total += group.LocalShapes.Length;
            }

            CollisionShape[] flattened = new CollisionShape[total];
            ulong[] keys = new ulong[groups.Count];
            int[] starts = new int[groups.Count];
            int written = 0;
            int groupIndex = 0;

            foreach (CollisionGroup group in groups.Values) {
                keys[groupIndex] = group.Key;
                starts[groupIndex] = written;
                written = PlaceGroup(group, flattened, written);
                groupIndex++;
            }

            statics = flattened;
            groupKeys = keys;
            groupStarts = starts;
            grid = new CollisionGrid(flattened);
            isDirty = false;
        }

        /// <summary>
        /// Copies one group's shapes into the flattened array at its offset. A zero offset copies the
        /// shapes untouched, so the scene group keeps its exported bits exactly.
        /// </summary>
        private static int PlaceGroup(CollisionGroup group, CollisionShape[] flattened, int written) {
            CollisionShape[] local = group.LocalShapes;
            bool isPlacedAsGiven = group.Offset == Vector3.Zero;

            for (int shapeIndex = 0; shapeIndex < local.Length; shapeIndex++) {
                flattened[written] = isPlacedAsGiven ? local[shapeIndex] : local[shapeIndex].Translated(group.Offset);
                written++;
            }

            return written;
        }

        /// <summary>The last group whose start is at or before the shape index.</summary>
        private int FindGroupIndex(int shapeIndex) {
            int low = 0;
            int high = groupStarts.Length - 1;

            while (low < high) {
                int middle = (low + high + 1) / 2;

                if (groupStarts[middle] <= shapeIndex) {
                    low = middle;
                    continue;
                }

                high = middle - 1;
            }

            return low;
        }

        /// <summary>One mover's authored shape, translated to where its path puts it this tick.</summary>
        private CollisionShape MoverWorldShape(int moverIndex, uint tick) {
            MoverDefinition mover = Movers[moverIndex];
            return mover.LocalShape.Translated(EvaluateMoverPosition(moverIndex, tick));
        }
    }
}
