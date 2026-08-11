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
    /// The wire form is every field in declaration order at full float precision. Geometry is exported
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
        /// The same shape moved by an offset. This is how a mover's local shape becomes world geometry:
        /// its pose is a pure translation of the authored primitive, which is why v1 movers do not rotate.
        /// </summary>
        public CollisionShape Translated(Vector3 offset) {
            CollisionShape moved = this;
            moved.Center = new Vector3(Center.X + offset.X, Center.Y + offset.Y, Center.Z + offset.Z);
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
            minX = Center.X - extentX;
            maxX = Center.X + extentX;
            minZ = Center.Z - extentZ;
            maxZ = Center.Z + extentZ;
        }

        /// <summary>Half the shape's width along world X and world Z, for the finite primitives.</summary>
        private void GetHorizontalExtents(out float extentX, out float extentZ) {
            if (Type == CollisionShapeType.Sphere) {
                extentX = Radius;
                extentZ = Radius;
                return;
            }

            if (Type == CollisionShapeType.Capsule) {
                extentX = MathF.Abs(AxisUp.X) * HalfLength + Radius;
                extentZ = MathF.Abs(AxisUp.Z) * HalfLength + Radius;
                return;
            }

            extentX = MathF.Abs(AxisRight.X) * HalfExtents.X
                + MathF.Abs(AxisUp.X) * HalfExtents.Y
                + MathF.Abs(AxisForward.X) * HalfExtents.Z;
            extentZ = MathF.Abs(AxisRight.Z) * HalfExtents.X
                + MathF.Abs(AxisUp.Z) * HalfExtents.Y
                + MathF.Abs(AxisForward.Z) * HalfExtents.Z;
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
        }
    }
}
