using System;
using System.Numerics;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// One piece of static or mover geometry, stored as a flat union: every primitive carries the same
    /// fields and only some of them mean anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A union rather than a class hierarchy because this is sim data. The resolver iterates an array of
    /// these in ascending index order every substep of every tick on both ends of the wire; a
    /// polymorphic call per shape would cost an indirection and, worse, would let the two runtimes
    /// devirtualise differently. A flat struct in a flat array is the same memory, in the same order, in
    /// both processes.
    /// </para>
    /// <para>
    /// <b>The basis is precomputed and orthonormal.</b> A box arrives from the exporter with its rotation
    /// already resolved into three unit axes, so nothing on the sim path ever calls a trig function to
    /// rebuild it. That is the single most important property of this type: rotation is authored data,
    /// not runtime arithmetic, and <see cref="MathF.Sin"/> differing by an ulp between Unity and .NET can
    /// therefore never move a wall.
    /// </para>
    /// <para>
    /// The wire form is every field in declaration order at full float precision; a prism appends its vertex
    /// count and its (x, z) pairs, so every other primitive keeps its exact bytes. The inline plan makes every shape
    /// about 204 bytes (140 before), copied by value. A reader built for fewer vertices rejects a longer plan, so
    /// <c>.geo</c> files and wire peers move to sixteen-vertex plans together. Geometry is exported
    /// once and hashed, not streamed per tick, so there is nothing to gain from quantizing it and a great
    /// deal to lose: a rounded wall is a wall in a different place on each end.
    /// </para>
    /// </remarks>
    public struct CollisionShape : INetMessage {
        /// <summary>Which primitive this is, and therefore which of the fields below are meaningful.</summary>
        public CollisionShapeType Type;

        /// <summary>Centre in world space. For a plane, only <c>Y</c> is read.</summary>
        public Vector3 Center;

        /// <summary>Box local +X as a unit vector. Unused by the other primitives.</summary>
        public Vector3 AxisRight;

        /// <summary>Box local +Y, or a capsule's segment direction. Unused by plane and sphere.</summary>
        public Vector3 AxisUp;

        /// <summary>Box local +Z as a unit vector. Unused by the other primitives.</summary>
        public Vector3 AxisForward;

        /// <summary>Box half-size along each of its own axes. Unused by the other primitives.</summary>
        public Vector3 HalfExtents;

        /// <summary>Sphere or capsule radius, in metres. Unused by plane and box.</summary>
        public float Radius;

        /// <summary>Half the capsule's segment length, measured from <see cref="Center"/>. Capsule only.</summary>
        public float HalfLength;

        /// <summary>A prism's plan polygon, as offsets from <see cref="Center"/> in world XZ. Prism only.</summary>
        public PrismPlan Plan;

        /// <summary>Shortest prism edge <see cref="MakePrism"/> accepts, metres.</summary>
        public const float MinPrismEdge = 1e-4f;

        /// <summary>Smallest turn cross product (<c>ex·fz − ez·fx</c>) at a prism vertex <see cref="MakePrism"/> accepts.</summary>
        public const float MinPrismTurn = 1e-7f;

        /// <summary>An infinite horizontal floor at <paramref name="height"/>.</summary>
        public static CollisionShape MakePlane(float height) {
            var shape = default(CollisionShape);
            shape.Type = CollisionShapeType.Plane;
            shape.Center = new Vector3(0f, height, 0f);
            shape.AxisUp = Vector3.UnitY;
            return shape;
        }

        /// <summary>
        /// An oriented box. The three axes must already be unit length and mutually perpendicular — the
        /// exporter is what guarantees that, by rejecting skewed transforms rather than normalising them.
        /// </summary>
        public static CollisionShape MakeBox(
            Vector3 center,
            Vector3 axisRight,
            Vector3 axisUp,
            Vector3 axisForward,
            Vector3 halfExtents) {
            var shape = default(CollisionShape);
            shape.Type = CollisionShapeType.Box;
            shape.Center = center;
            shape.AxisRight = axisRight;
            shape.AxisUp = axisUp;
            shape.AxisForward = axisForward;
            shape.HalfExtents = halfExtents;
            return shape;
        }

        /// <summary>A sphere.</summary>
        public static CollisionShape MakeSphere(Vector3 center, float radius) {
            var shape = default(CollisionShape);
            shape.Type = CollisionShapeType.Sphere;
            shape.Center = center;
            shape.AxisUp = Vector3.UnitY;
            shape.Radius = radius;
            return shape;
        }

        /// <summary>A capsule whose segment runs <paramref name="halfLength"/> either side of the centre along <paramref name="axisUp"/>.</summary>
        public static CollisionShape MakeCapsule(Vector3 center, Vector3 axisUp, float halfLength, float radius) {
            var shape = default(CollisionShape);
            shape.Type = CollisionShapeType.Capsule;
            shape.Center = center;
            shape.AxisUp = axisUp;
            shape.HalfLength = halfLength;
            shape.Radius = radius;
            return shape;
        }

        /// <summary>
        /// A vertical convex prism: the plan <paramref name="offsets"/> (x, z) around <paramref name="center"/>,
        /// extruded <paramref name="halfHeight"/> above and below it. Yaw is baked into the offsets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The offsets must be strictly convex and counter-clockwise by sign: shoelace area &gt; 0 and every turn
        /// cross <c>ex·fz − ez·fx</c> &gt; <see cref="MinPrismTurn"/>, so the outward normal of edge (dx, dz) is
        /// (dz, −dx). Every vertex must also sit inside every edge it is not on, which rules out a polygon
        /// that winds twice. Anything else throws <see cref="ArgumentException"/>.
        /// </para>
        /// <para>
        /// The plan is recentred on its bounding box, moving <see cref="Center"/> by the same amount, so
        /// <see cref="HalfExtents"/> X and Z are the box's half sizes and identity axes let the box path answer
        /// <see cref="Translated"/>, <see cref="GetBoundsXZ"/> and the resolver's vertical reach.
        /// <see cref="Radius"/> bounds the farthest vertex from the centre (rounded up to 1e-4, plus 1e-4), an early-reject
        /// bound. Centre, offsets and radius are computed in double and cast once so every runtime gets the same bits.
        /// </para>
        /// <para>
        /// A mirrored placement (negative scale on one plan axis) flips the winding: reverse the vertex order before
        /// calling, or this throws.
        /// </para>
        /// </remarks>
        public static CollisionShape MakePrism(Vector3 center, float halfHeight, ReadOnlySpan<Vector2> offsets) {
            if (offsets.Length < PrismPlan.MinVertices || offsets.Length > PrismPlan.MaxVertices) {
                throw new ArgumentException("A prism needs 3 to 16 plan vertices.", nameof(offsets));
            }

            if (!IsFinite(center.X) || !IsFinite(center.Y) || !IsFinite(center.Z) || !IsFinite(halfHeight) || !(halfHeight > 0f)) {
                throw new ArgumentException("A prism needs a finite centre and a positive finite half height.", nameof(halfHeight));
            }

            GetPlanBounds(offsets, out float minX, out float maxX, out float minZ, out float maxZ);
            // Double maths cast once: Mono may widen float intermediates where CoreCLR does not.
            double middleX = ((double)minX + maxX) * 0.5;
            double middleZ = ((double)minZ + maxZ) * 0.5;
            var plan = default(PrismPlan);
            plan.Count = (byte)offsets.Length;
            double radiusSquared = 0.0;
            for (int index = 0; index < offsets.Length; index++) {
                float x = (float)(offsets[index].X - middleX);
                float z = (float)(offsets[index].Y - middleZ);
                plan.SetVertex(index, x, z);
                radiusSquared = Math.Max(radiusSquared, (double)x * x + (double)z * z);
            }

            ValidatePrismPlan(in plan);
            var shape = default(CollisionShape);
            shape.Type = CollisionShapeType.Prism;
            shape.Center = new Vector3((float)(center.X + middleX), center.Y, (float)(center.Z + middleZ));
            shape.AxisRight = Vector3.UnitX;
            shape.AxisUp = Vector3.UnitY;
            shape.AxisForward = Vector3.UnitZ;
            shape.HalfExtents = new Vector3((maxX - minX) * 0.5f, halfHeight, (maxZ - minZ) * 0.5f);
            shape.Radius = PrismRadius(radiusSquared);
            shape.Plan = plan;
            return shape;
        }

        /// <summary>
        /// The same shape moved by an offset. This is how a mover's local shape becomes world geometry:
        /// its pose is a pure translation of the authored primitive, which is why v1 movers do not rotate.
        /// </summary>
        public CollisionShape Translated(Vector3 offset) {
            CollisionShape moved = this;
            moved.Center = new Vector3((float)(Center.X + offset.X), (float)(Center.Y + offset.Y), (float)(Center.Z + offset.Z));
            return moved;
        }

        /// <summary>
        /// The shape's footprint on the XZ plane, for broad-phase bucketing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A plane has no finite footprint; it reports the full float range so that the grid treats it as
        /// present in every cell it is asked about, which is exactly what an infinite floor should do.
        /// The range is <see cref="float.MinValue"/> to <see cref="float.MaxValue"/> rather than the
        /// infinities, because the grid divides these numbers by its cell size and an infinity that meets
        /// a subtraction on the way there turns into a NaN that silently swallows every bucket.
        /// </para>
        /// <para>
        /// The box case is the only interesting one: a rotated box's footprint is the sum of its three
        /// half extents projected onto the axis, which is what taking the absolute value of each basis
        /// component and multiplying by the matching half extent computes. It is a conservative bound for
        /// a rotated box — that is the point, since a broad phase may over-report and may never
        /// under-report.
        /// </para>
        /// </remarks>
        public void GetBoundsXZ(out float minX, out float maxX, out float minZ, out float maxZ) {
            if (Type == CollisionShapeType.Plane) {
                minX = float.MinValue;
                maxX = float.MaxValue;
                minZ = float.MinValue;
                maxZ = float.MaxValue;
                return;
            }

            GetHorizontalExtents(out float extentX, out float extentZ);
            minX = (float)(Center.X - extentX);
            maxX = (float)(Center.X + extentX);
            minZ = (float)(Center.Z - extentZ);
            maxZ = (float)(Center.Z + extentZ);
        }

        /// <summary>Half the shape's width along world X and world Z, for the finite primitives.</summary>
        private void GetHorizontalExtents(out float extentX, out float extentZ) {
            if (Type == CollisionShapeType.Sphere) {
                extentX = Radius;
                extentZ = Radius;
                return;
            }

            if (Type == CollisionShapeType.Capsule) {
                extentX = (float)((float)(MathF.Abs(AxisUp.X) * HalfLength) + Radius);
                extentZ = (float)((float)(MathF.Abs(AxisUp.Z) * HalfLength) + Radius);
                return;
            }

            float rightX = (float)(MathF.Abs(AxisRight.X) * HalfExtents.X);
            float upX = (float)(MathF.Abs(AxisUp.X) * HalfExtents.Y);
            float forwardX = (float)(MathF.Abs(AxisForward.X) * HalfExtents.Z);
            extentX = (float)((float)(rightX + upX) + forwardX);
            float rightZ = (float)(MathF.Abs(AxisRight.Z) * HalfExtents.X);
            float upZ = (float)(MathF.Abs(AxisUp.Z) * HalfExtents.Y);
            float forwardZ = (float)(MathF.Abs(AxisForward.Z) * HalfExtents.Z);
            extentZ = (float)((float)(rightZ + upZ) + forwardZ);
        }

        // The early-reject radius rounded up to the 1e-4 grid plus one step, so the float cast never undercuts it. Holds
        // up to a radius of about 2 km, where half a float ulp outgrows the 1e-4 margin; prisms are far smaller.
        private static float PrismRadius(double radiusSquared) {
            return (float)(Math.Ceiling(Math.Sqrt(radiusSquared) * 10000.0) / 10000.0 + 0.0001);
        }

        // The offsets' bounding box; throws on a non-finite offset.
        private static void GetPlanBounds(ReadOnlySpan<Vector2> offsets, out float minX, out float maxX, out float minZ, out float maxZ) {
            minX = float.MaxValue;
            maxX = float.MinValue;
            minZ = float.MaxValue;
            maxZ = float.MinValue;
            for (int index = 0; index < offsets.Length; index++) {
                Vector2 offset = offsets[index];
                if (!IsFinite(offset.X) || !IsFinite(offset.Y)) {
                    throw new ArgumentException("Prism plan offsets must be finite.", nameof(offsets));
                }

                minX = MathF.Min(minX, offset.X);
                maxX = MathF.Max(maxX, offset.X);
                minZ = MathF.Min(minZ, offset.Y);
                maxZ = MathF.Max(maxZ, offset.Y);
            }
        }

        // Throws unless every edge is long enough and the polygon strictly convex CCW.
        private static void ValidatePrismPlan(in PrismPlan plan) {
            int count = plan.Count;
            float doubleArea = 0f;
            for (int index = 0; index < count; index++) {
                plan.GetVertex(index, out float x, out float z);
                plan.GetVertex((index + 1) % count, out float nextX, out float nextZ);
                plan.GetVertex((index + 2) % count, out float afterX, out float afterZ);
                float edgeX = (float)(nextX - x);
                float edgeZ = (float)(nextZ - z);
                if ((float)((float)(edgeX * edgeX) + (float)(edgeZ * edgeZ)) <= (float)(MinPrismEdge * MinPrismEdge)) {
                    throw new ArgumentException("A prism plan edge is shorter than 1e-4.", nameof(plan));
                }

                float turn = (float)((float)(edgeX * (float)(afterZ - nextZ)) - (float)(edgeZ * (float)(afterX - nextX)));
                if (!(turn > MinPrismTurn)) {
                    throw new ArgumentException("A prism plan must be strictly convex and counter-clockwise.", nameof(plan));
                }

                doubleArea = (float)(doubleArea + (float)((float)(x * nextZ) - (float)(nextX * z)));
                ValidateEdgeHoldsPlan(in plan, index);
            }

            if (!(doubleArea > 0f)) {
                throw new ArgumentException("A prism plan must have positive (counter-clockwise) area.", nameof(plan));
            }
        }

        // Every vertex off edge `edge` lies strictly on its inner side; a polygon winding twice fails here.
        private static void ValidateEdgeHoldsPlan(in PrismPlan plan, int edge) {
            int count = plan.Count;
            plan.GetVertex(edge, out float x, out float z);
            plan.GetVertex((edge + 1) % count, out float nextX, out float nextZ);
            for (int other = 2; other < count; other++) {
                plan.GetVertex((edge + other) % count, out float otherX, out float otherZ);
                float cross = (float)((float)((float)(nextX - x) * (float)(otherZ - z)) - (float)((float)(nextZ - z) * (float)(otherX - x)));
                if (!(cross > 0f)) {
                    throw new ArgumentException("A prism plan must be a simple convex polygon.", nameof(plan));
                }
            }
        }

        private static bool IsFinite(float value) {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteByte((byte)Type);
            writer.WriteVector3(Center);
            writer.WriteVector3(AxisRight);
            writer.WriteVector3(AxisUp);
            writer.WriteVector3(AxisForward);
            writer.WriteVector3(HalfExtents);
            writer.WriteFloat(Radius);
            writer.WriteFloat(HalfLength);
            if (Type != CollisionShapeType.Prism) {
                return;
            }

            writer.WriteByte(Plan.Count);
            for (int index = 0; index < Plan.Count; index++) {
                Plan.GetVertex(index, out float x, out float z);
                writer.WriteFloat(x);
                writer.WriteFloat(z);
            }
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Type = (CollisionShapeType)reader.ReadByte();
            Center = reader.ReadVector3();
            AxisRight = reader.ReadVector3();
            AxisUp = reader.ReadVector3();
            AxisForward = reader.ReadVector3();
            HalfExtents = reader.ReadVector3();
            Radius = reader.ReadFloat();
            HalfLength = reader.ReadFloat();
            Plan = default;
            if (Type != CollisionShapeType.Prism) {
                return;
            }

            // Range-checked before any float is read: these bytes may come off the network.
            byte count = reader.ReadByte();
            if (count < PrismPlan.MinVertices || count > PrismPlan.MaxVertices) {
                throw new NetProtocolException("Prism plan declares " + count + " vertices; 3 to 16 are allowed.");
            }

            Plan.Count = count;
            for (int index = 0; index < count; index++) {
                float x = reader.ReadFloat();
                float z = reader.ReadFloat();
                Plan.SetVertex(index, x, z);
            }

            RejectShortEdges(in Plan);
        }

        // A zero-length edge would give the resolver a NaN signed distance; refuse it like a bad count.
        private static void RejectShortEdges(in PrismPlan plan) {
            for (int index = 0; index < plan.Count; index++) {
                plan.GetVertex(index, out float x, out float z);
                plan.GetVertex((index + 1) % plan.Count, out float nextX, out float nextZ);
                float edgeX = (float)(nextX - x);
                float edgeZ = (float)(nextZ - z);
                if (!((float)((float)(edgeX * edgeX) + (float)(edgeZ * edgeZ)) > (float)(MinPrismEdge * MinPrismEdge))) {
                    throw new NetProtocolException("Prism plan edge " + index + " is shorter than 1e-4.");
                }
            }
        }
    }
}
