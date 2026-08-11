using System;
using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The narrow phase: capsule against one primitive, and vertical probe against one primitive. Nothing
    /// else in the collision system does geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keeping both queries here — rather than spreading them across the shapes they test — means there
    /// is exactly one implementation of each primitive's arithmetic, shared by the server, the predicting
    /// client and the tests. A capsule-versus-box that lives in two places is a capsule-versus-box that
    /// eventually disagrees with itself.
    /// </para>
    /// <para>
    /// <b>Determinism rules this file obeys, and any edit must keep.</b> Single-precision floats only,
    /// never <see cref="double"/>. A fixed operation order with no reassociation — the compiler is
    /// permitted to fold constants but the source must not leave the grouping ambiguous. The only
    /// arithmetic allowed on the position path is <c>+ - * /</c> plus <see cref="MathF.Sqrt"/>,
    /// <see cref="MathF.Min"/>, <see cref="MathF.Max"/> and <see cref="MathF.Abs"/>; no trigonometry, no
    /// <see cref="MathF.Pow"/>, no normalisation helper that might be implemented differently on the two
    /// runtimes. That last rule is why every dot product, subtraction and normalise in this file is
    /// written out componentwise instead of calling <see cref="Vector3.Dot"/> or
    /// <see cref="Vector3.Normalize"/>: those are free to reach for SIMD instructions whose accumulation
    /// order is the JIT's business, not ours. Shapes are visited by the caller in ascending index order,
    /// so a capsule wedged into a corner resolves the same corner first on both ends.
    /// </para>
    /// <para>
    /// The one loop in here that could have been an iterative solver — finding where along the pawn's
    /// segment it comes closest to a box — is instead an exact scan over a fixed, small set of candidate
    /// parameters. Distance from a segment to a box is convex and piecewise quadratic, so its minimum is
    /// either at a slab crossing or at the vertex of one quadratic piece, and enumerating those is both
    /// exact and free of any "iterate until it stops changing" that could stop one iteration later on a
    /// different machine.
    /// </para>
    /// </remarks>
    public static class CollisionResolver {
        /// <summary>
        /// The shape index a freshly produced contact or support carries. The resolver is handed one
        /// shape and has no idea where it came from; <c>CollisionWorld</c> stamps the real provenance —
        /// index, mover flag, mover index — on the way out. Anything holding a contact with this value
        /// still in it has skipped that step.
        /// </summary>
        public const int UnassignedShapeIndex = -1;

        /// <summary>Below this magnitude a direction component counts as parallel and its slab is skipped.</summary>
        private const float ParallelEpsilon = 1e-8f;

        /// <summary>
        /// Squared distance below which a separation vector is too short to take a direction from, and the
        /// caller falls back to a rule that does not need one.
        /// </summary>
        private const float DegenerateSquaredEpsilon = 1e-12f;

        /// <summary>
        /// Room for the box parameter scan: two segment ends, six slab crossings, and the quadratic vertex
        /// of each of the at-most-seven intervals those eight points bound.
        /// </summary>
        private const int MaxBoxParameters = 16;

        /// <summary>How far above a shape's highest point a downward support probe starts, in metres.</summary>
        private const float ProbeClearance = 1f;

        /// <summary>
        /// Tests one capsule against one shape and, when they overlap, describes the shortest push that
        /// separates them.
        /// </summary>
        /// <remarks>
        /// Touching exactly is not overlapping: a capsule whose surface grazes a wall at zero depth
        /// reports no contact, because a zero-depth push is not a push and pretending otherwise would have
        /// the motor jitter every frame a pawn rests against something.
        /// </remarks>
        /// <param name="pose">The pawn's capsule this substep.</param>
        /// <param name="shape">The primitive to test against.</param>
        /// <param name="contact">The separating push, valid only when this returns true.</param>
        /// <returns>True when the two overlap.</returns>
        public static bool TryGetContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            if (shape.Type == CollisionShapeType.Plane) {
                return TryGetPlaneContact(pose, shape, out contact);
            }

            if (shape.Type == CollisionShapeType.Box) {
                return TryGetBoxContact(pose, shape, out contact);
            }

            if (shape.Type == CollisionShapeType.Sphere) {
                return TryGetSphereContact(pose, shape, out contact);
            }

            if (shape.Type == CollisionShapeType.Capsule) {
                return TryGetCapsuleContact(pose, shape, out contact);
            }

            contact = default;
            return false;
        }

        /// <summary>
        /// Finds the highest point of a shape directly under a horizontal position, within a vertical
        /// span.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the grounding, ground-clamping and step-up query all at once. The span is what makes
        /// it all three: probing from a little above the feet to a little below them finds a ledge the
        /// pawn may step onto and a floor it may be clamped down to with the same call, and the caller
        /// decides which by comparing the height it gets back with the feet it started from.
        /// </para>
        /// <para>
        /// A shape has exactly one answer per column — its <b>topmost</b> surface at that x and z — and
        /// the span only decides whether that answer is reported. A pawn buried inside a box therefore
        /// gets no support from it rather than support from its underside, and depenetration, not the
        /// ground clamp, is what gets it out. The span is inclusive at both ends so that a pawn standing
        /// exactly on the edge of its own tolerance is standing, not falling.
        /// </para>
        /// </remarks>
        /// <param name="shape">The primitive to probe.</param>
        /// <param name="x">Horizontal probe position.</param>
        /// <param name="z">Horizontal probe position.</param>
        /// <param name="probeTop">Top of the vertical span, in world units.</param>
        /// <param name="probeBottom">Bottom of the vertical span, in world units.</param>
        /// <param name="height">Surface height found, valid only when this returns true.</param>
        /// <param name="normal">Upward normal at that point, valid only when this returns true.</param>
        /// <returns>True when the shape has a surface inside the span.</returns>
        public static bool TryGetSupport(
            in CollisionShape shape,
            float x,
            float z,
            float probeTop,
            float probeBottom,
            out float height,
            out Vector3 normal) {
            if (!TryGetSurfaceHeight(shape, x, z, out height, out normal)) {
                return false;
            }

            if (height > probeTop) {
                return false;
            }

            return height >= probeBottom;
        }

        /// <summary>An infinite floor: the feet are the only part of the capsule that can be below it.</summary>
        private static bool TryGetPlaneContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            contact = default;
            float depth = shape.Center.Y - pose.FootPosition.Y;
            if (depth <= 0f) {
                return false;
            }

            contact = new CollisionContact(new Vector3(0f, 1f, 0f), depth, UnassignedShapeIndex);
            return true;
        }

        /// <summary>
        /// Capsule against sphere, which is the same question as point against sphere once the point is
        /// the place on the capsule's segment nearest the sphere's centre.
        /// </summary>
        private static bool TryGetSphereContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            Vector3 segmentBottom = pose.SegmentBottom;
            Vector3 segmentTop = pose.SegmentTop;
            float parameter = ClosestParameterOnSegment(segmentBottom, segmentTop, shape.Center);
            Vector3 nearest = AddScaled(segmentBottom, Subtract(segmentTop, segmentBottom), parameter);
            return TryResolveSeparation(nearest, shape.Center, pose.Radius + shape.Radius, out contact);
        }

        /// <summary>
        /// Capsule against capsule: the closest pair of points on the two segments, then the same radius
        /// arithmetic every other primitive ends in.
        /// </summary>
        private static bool TryGetCapsuleContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            Vector3 poseBottom = pose.SegmentBottom;
            Vector3 poseTop = pose.SegmentTop;
            GetShapeSegment(shape, out Vector3 shapeBottom, out Vector3 shapeTop);
            ClosestSegmentParameters(poseBottom, poseTop, shapeBottom, shapeTop, out float poseParameter, out float shapeParameter);
            Vector3 onPose = AddScaled(poseBottom, Subtract(poseTop, poseBottom), poseParameter);
            Vector3 onShape = AddScaled(shapeBottom, Subtract(shapeTop, shapeBottom), shapeParameter);
            return TryResolveSeparation(onPose, onShape, pose.Radius + shape.Radius, out contact);
        }

        /// <summary>
        /// Capsule against oriented box. The whole test happens in the box's own frame, where the box is
        /// an axis-aligned span and the capsule is an arbitrary segment; the basis that gets it there is
        /// authored data, so no rotation is ever reconstructed here.
        /// </summary>
        private static bool TryGetBoxContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            Vector3 localBottom = ToBoxLocal(shape, pose.SegmentBottom);
            Vector3 localTop = ToBoxLocal(shape, pose.SegmentTop);
            Vector3 localDirection = Subtract(localTop, localBottom);
            float parameter = ClosestSegmentParameterToBox(localBottom, localDirection, shape.HalfExtents);
            Vector3 localPoint = AddScaled(localBottom, localDirection, parameter);
            return TryResolveBoxOverlap(shape, localPoint, pose.Radius, out contact);
        }

        /// <summary>
        /// Sphere against axis-aligned span, in box-local coordinates: outside the span the push is along
        /// the vector to the nearest surface point, inside it there is no such vector and the push is out
        /// of whichever face is nearest.
        /// </summary>
        private static bool TryResolveBoxOverlap(in CollisionShape shape, in Vector3 localPoint, float radius, out CollisionContact contact) {
            contact = default;
            Vector3 halfExtents = shape.HalfExtents;
            float offsetX = localPoint.X - Clamp(localPoint.X, -halfExtents.X, halfExtents.X);
            float offsetY = localPoint.Y - Clamp(localPoint.Y, -halfExtents.Y, halfExtents.Y);
            float offsetZ = localPoint.Z - Clamp(localPoint.Z, -halfExtents.Z, halfExtents.Z);
            float distanceSquared = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;
            if (distanceSquared >= radius * radius) {
                return false;
            }

            if (distanceSquared <= DegenerateSquaredEpsilon) {
                return TryResolveBoxInterior(shape, localPoint, radius, out contact);
            }

            float distance = MathF.Sqrt(distanceSquared);
            var localNormal = new Vector3(offsetX / distance, offsetY / distance, offsetZ / distance);
            contact = new CollisionContact(ToWorldDirection(shape, localNormal), radius - distance, UnassignedShapeIndex);
            return true;
        }

        /// <summary>
        /// The capsule's centre line is inside the box. Push out of the nearest face — ties broken X, then
        /// Y, then Z, so that a pawn wedged into a perfect corner picks the same escape on both ends of
        /// the wire.
        /// </summary>
        private static bool TryResolveBoxInterior(in CollisionShape shape, in Vector3 localPoint, float radius, out CollisionContact contact) {
            Vector3 halfExtents = shape.HalfExtents;
            float depthX = halfExtents.X - MathF.Abs(localPoint.X);
            float depthY = halfExtents.Y - MathF.Abs(localPoint.Y);
            float depthZ = halfExtents.Z - MathF.Abs(localPoint.Z);
            float shallowest = MathF.Min(depthX, MathF.Min(depthY, depthZ));
            Vector3 localNormal = SelectFaceNormal(localPoint, depthX, depthY, shallowest);
            contact = new CollisionContact(ToWorldDirection(shape, localNormal), shallowest + radius, UnassignedShapeIndex);
            return true;
        }

        /// <summary>Which face the shallowest penetration belongs to, signed toward the side the point is on.</summary>
        private static Vector3 SelectFaceNormal(in Vector3 localPoint, float depthX, float depthY, float shallowest) {
            if (depthX <= shallowest) {
                return new Vector3(SignOf(localPoint.X), 0f, 0f);
            }

            if (depthY <= shallowest) {
                return new Vector3(0f, SignOf(localPoint.Y), 0f);
            }

            return new Vector3(0f, 0f, SignOf(localPoint.Z));
        }

        /// <summary>
        /// Turns two nearest points and a radius sum into a contact, or into nothing when they are far
        /// enough apart. Shared by the sphere and capsule cases, which differ only in how they found the
        /// points.
        /// </summary>
        private static bool TryResolveSeparation(in Vector3 onCapsule, in Vector3 onShape, float radiusSum, out CollisionContact contact) {
            contact = default;
            float offsetX = onCapsule.X - onShape.X;
            float offsetY = onCapsule.Y - onShape.Y;
            float offsetZ = onCapsule.Z - onShape.Z;
            float distanceSquared = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;
            if (distanceSquared >= radiusSum * radiusSum) {
                return false;
            }

            if (distanceSquared <= DegenerateSquaredEpsilon) {
                contact = new CollisionContact(new Vector3(0f, 1f, 0f), radiusSum, UnassignedShapeIndex);
                return true;
            }

            float distance = MathF.Sqrt(distanceSquared);
            var normal = new Vector3(offsetX / distance, offsetY / distance, offsetZ / distance);
            contact = new CollisionContact(normal, radiusSum - distance, UnassignedShapeIndex);
            return true;
        }

        /// <summary>
        /// Where along the capsule's segment it comes closest to the box, as a parameter in
        /// <c>[0, 1]</c>, all in box-local coordinates.
        /// </summary>
        /// <remarks>
        /// The candidates are the two segment ends, the six points where the segment crosses a slab
        /// boundary, and the vertex of the quadratic on each interval those eight bound. Between two
        /// neighbouring crossings the vector from the segment to the box is affine in the parameter, so
        /// its squared length is an honest quadratic and its vertex is one division away. When the segment
        /// passes clean through the box every candidate ties at zero distance, and the tie is broken by
        /// depth instead — the deepest point is the one whose face normal is worth pushing along.
        /// </remarks>
        private static float ClosestSegmentParameterToBox(in Vector3 origin, in Vector3 direction, in Vector3 halfExtents) {
            Span<float> parameters = stackalloc float[MaxBoxParameters];
            int count = 0;
            parameters[count] = 0f;
            count++;
            parameters[count] = 1f;
            count++;
            AppendSlabCrossings(origin.X, direction.X, halfExtents.X, parameters, ref count);
            AppendSlabCrossings(origin.Y, direction.Y, halfExtents.Y, parameters, ref count);
            AppendSlabCrossings(origin.Z, direction.Z, halfExtents.Z, parameters, ref count);

            int breakpointCount = count;
            SortAscending(parameters.Slice(0, breakpointCount));
            for (int index = 0; index + 1 < breakpointCount; index++) {
                parameters[count] = IntervalVertexParameter(origin, direction, halfExtents, parameters[index], parameters[index + 1]);
                count++;
            }

            return SelectBestParameter(origin, direction, halfExtents, parameters, count);
        }

        /// <summary>Adds the two parameters at which one coordinate crosses its slab boundaries, clamped to the segment.</summary>
        private static void AppendSlabCrossings(float origin, float direction, float halfExtent, Span<float> parameters, ref int count) {
            if (MathF.Abs(direction) <= ParallelEpsilon) {
                return;
            }

            float inverse = 1f / direction;
            parameters[count] = Saturate((-halfExtent - origin) * inverse);
            count++;
            parameters[count] = Saturate((halfExtent - origin) * inverse);
            count++;
        }

        /// <summary>
        /// The parameter minimising the squared distance to the box within one interval of constant
        /// clamping, clamped back into that interval.
        /// </summary>
        private static float IntervalVertexParameter(
            in Vector3 origin,
            in Vector3 direction,
            in Vector3 halfExtents,
            float lower,
            float upper) {
            float middle = (lower + upper) * 0.5f;
            Vector3 midpoint = AddScaled(origin, direction, middle);
            GetAffineOffset(midpoint.X, origin.X, direction.X, halfExtents.X, out float constantX, out float slopeX);
            GetAffineOffset(midpoint.Y, origin.Y, direction.Y, halfExtents.Y, out float constantY, out float slopeY);
            GetAffineOffset(midpoint.Z, origin.Z, direction.Z, halfExtents.Z, out float constantZ, out float slopeZ);
            float slopeSquared = slopeX * slopeX + slopeY * slopeY + slopeZ * slopeZ;
            if (slopeSquared <= 0f) {
                return lower;
            }

            float projection = constantX * slopeX + constantY * slopeY + constantZ * slopeZ;
            return Clamp(-projection / slopeSquared, lower, upper);
        }

        /// <summary>
        /// One coordinate of the segment-to-box offset, written as <c>constant + slope * parameter</c>
        /// for the interval the sample point sits in. A coordinate inside its slab contributes nothing.
        /// </summary>
        private static void GetAffineOffset(
            float sample,
            float origin,
            float direction,
            float halfExtent,
            out float constant,
            out float slope) {
            if (sample > halfExtent) {
                constant = origin - halfExtent;
                slope = direction;
                return;
            }

            if (sample < -halfExtent) {
                constant = origin + halfExtent;
                slope = direction;
                return;
            }

            constant = 0f;
            slope = 0f;
        }

        /// <summary>Picks the closest candidate, or the deepest one when the segment is inside the box.</summary>
        private static float SelectBestParameter(
            in Vector3 origin,
            in Vector3 direction,
            in Vector3 halfExtents,
            Span<float> parameters,
            int count) {
            float closestParameter = 0f;
            float smallestDistanceSquared = float.MaxValue;
            float deepestParameter = 0f;
            float greatestInteriorDepth = float.MinValue;
            for (int index = 0; index < count; index++) {
                float parameter = parameters[index];
                Vector3 point = AddScaled(origin, direction, parameter);
                float distanceSquared = SquaredDistanceToBox(point, halfExtents);
                float interiorDepth = InteriorDepth(point, halfExtents);
                if (distanceSquared < smallestDistanceSquared) {
                    smallestDistanceSquared = distanceSquared;
                    closestParameter = parameter;
                }

                if (interiorDepth > greatestInteriorDepth) {
                    greatestInteriorDepth = interiorDepth;
                    deepestParameter = parameter;
                }
            }

            if (smallestDistanceSquared > DegenerateSquaredEpsilon) {
                return closestParameter;
            }

            return deepestParameter;
        }

        /// <summary>Squared distance from a box-local point to the box, zero inside it.</summary>
        private static float SquaredDistanceToBox(in Vector3 point, in Vector3 halfExtents) {
            float offsetX = point.X - Clamp(point.X, -halfExtents.X, halfExtents.X);
            float offsetY = point.Y - Clamp(point.Y, -halfExtents.Y, halfExtents.Y);
            float offsetZ = point.Z - Clamp(point.Z, -halfExtents.Z, halfExtents.Z);
            return offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;
        }

        /// <summary>How far a box-local point is from the nearest face, negative when it is outside.</summary>
        private static float InteriorDepth(in Vector3 point, in Vector3 halfExtents) {
            float depthX = halfExtents.X - MathF.Abs(point.X);
            float depthY = halfExtents.Y - MathF.Abs(point.Y);
            float depthZ = halfExtents.Z - MathF.Abs(point.Z);
            return MathF.Min(depthX, MathF.Min(depthY, depthZ));
        }

        /// <summary>The topmost surface of one shape in one vertical column, before any span test.</summary>
        private static bool TryGetSurfaceHeight(in CollisionShape shape, float x, float z, out float height, out Vector3 normal) {
            if (shape.Type == CollisionShapeType.Plane) {
                height = shape.Center.Y;
                normal = new Vector3(0f, 1f, 0f);
                return true;
            }

            if (shape.Type == CollisionShapeType.Box) {
                return TryGetBoxSurfaceHeight(shape, x, z, out height, out normal);
            }

            if (shape.Type == CollisionShapeType.Sphere) {
                return TryGetSphereSurfaceHeight(shape.Center, shape.Radius, x, z, out height, out normal);
            }

            if (shape.Type == CollisionShapeType.Capsule) {
                return TryGetCapsuleSurfaceHeight(shape, x, z, out height, out normal);
            }

            height = 0f;
            normal = new Vector3(0f, 1f, 0f);
            return false;
        }

        /// <summary>
        /// Drops a ray down the column and reports the face it enters through. The ray starts a metre
        /// above everything the box can reach, so the entry face is always an upward-facing one and the
        /// distance travelled converts straight into a world height.
        /// </summary>
        private static bool TryGetBoxSurfaceHeight(in CollisionShape shape, float x, float z, out float height, out Vector3 normal) {
            height = 0f;
            normal = new Vector3(0f, 1f, 0f);
            float startHeight = shape.Center.Y + VerticalReach(shape) + ProbeClearance;
            Vector3 localOrigin = ToBoxLocal(shape, new Vector3(x, startHeight, z));
            var localDirection = new Vector3(-shape.AxisRight.Y, -shape.AxisUp.Y, -shape.AxisForward.Y);
            float entry = 0f;
            float exit = float.MaxValue;
            int entryAxis = -1;
            float entrySign = 1f;
            if (!TryClipSlab(localOrigin.X, localDirection.X, shape.HalfExtents.X, 0, ref entry, ref exit, ref entryAxis, ref entrySign)) {
                return false;
            }

            if (!TryClipSlab(localOrigin.Y, localDirection.Y, shape.HalfExtents.Y, 1, ref entry, ref exit, ref entryAxis, ref entrySign)) {
                return false;
            }

            if (!TryClipSlab(localOrigin.Z, localDirection.Z, shape.HalfExtents.Z, 2, ref entry, ref exit, ref entryAxis, ref entrySign)) {
                return false;
            }

            if (entryAxis < 0) {
                return false;
            }

            height = startHeight - entry;
            normal = ScaledAxis(shape, entryAxis, entrySign);
            return true;
        }

        /// <summary>
        /// Clips a ray against one slab of the box, narrowing the entry and exit distances and
        /// remembering which face the entry came through. Returns false once the two have crossed, which
        /// is the ray missing the box.
        /// </summary>
        private static bool TryClipSlab(
            float origin,
            float direction,
            float halfExtent,
            int axisIndex,
            ref float entry,
            ref float exit,
            ref int entryAxis,
            ref float entrySign) {
            if (MathF.Abs(direction) <= ParallelEpsilon) {
                return MathF.Abs(origin) <= halfExtent;
            }

            float inverse = 1f / direction;
            float toLow = (-halfExtent - origin) * inverse;
            float toHigh = (halfExtent - origin) * inverse;
            float near = MathF.Min(toLow, toHigh);
            float far = MathF.Max(toLow, toHigh);
            if (near > entry) {
                entry = near;
                entryAxis = axisIndex;
                entrySign = direction < 0f ? 1f : -1f;
            }

            exit = MathF.Min(exit, far);
            return entry <= exit;
        }

        /// <summary>The top of a sphere in one column, and the surface normal there — which is simply the radius direction.</summary>
        private static bool TryGetSphereSurfaceHeight(in Vector3 center, float radius, float x, float z, out float height, out Vector3 normal) {
            height = 0f;
            normal = new Vector3(0f, 1f, 0f);
            float offsetX = x - center.X;
            float offsetZ = z - center.Z;
            float planarSquared = offsetX * offsetX + offsetZ * offsetZ;
            float radiusSquared = radius * radius;
            if (planarSquared > radiusSquared) {
                return false;
            }

            float rise = MathF.Sqrt(radiusSquared - planarSquared);
            height = center.Y + rise;
            normal = new Vector3(offsetX / radius, rise / radius, offsetZ / radius);
            return true;
        }

        /// <summary>
        /// A capsule's top in one column is the highest of three answers — its barrel and its two caps —
        /// so all three are asked and the best one wins. Asking them in that fixed order and keeping the
        /// strictly higher result makes the tie between a cap and the barrel they share resolve the same
        /// way every time.
        /// </summary>
        private static bool TryGetCapsuleSurfaceHeight(in CollisionShape shape, float x, float z, out float height, out Vector3 normal) {
            GetShapeSegment(shape, out Vector3 lowerCenter, out Vector3 upperCenter);
            bool hasBarrel = TryGetCapsuleBarrelHeight(shape, x, z, out float barrelHeight, out Vector3 barrelNormal);
            bool hasLowerCap = TryGetSphereSurfaceHeight(lowerCenter, shape.Radius, x, z, out float lowerHeight, out Vector3 lowerNormal);
            bool hasUpperCap = TryGetSphereSurfaceHeight(upperCenter, shape.Radius, x, z, out float upperHeight, out Vector3 upperNormal);
            bool found = false;
            height = float.MinValue;
            normal = new Vector3(0f, 1f, 0f);
            if (hasBarrel) {
                height = barrelHeight;
                normal = barrelNormal;
                found = true;
            }

            if (hasLowerCap && lowerHeight > height) {
                height = lowerHeight;
                normal = lowerNormal;
                found = true;
            }

            if (hasUpperCap && upperHeight > height) {
                height = upperHeight;
                normal = upperNormal;
                found = true;
            }

            if (!found) {
                height = 0f;
            }

            return found;
        }

        /// <summary>
        /// Where the column meets the capsule's barrel, solved directly rather than by raycasting: the
        /// condition "this point is one radius from the axis" is a quadratic in height once the column's
        /// x and z are fixed, and its larger root is the top of the barrel.
        /// </summary>
        private static bool TryGetCapsuleBarrelHeight(in CollisionShape shape, float x, float z, out float height, out Vector3 normal) {
            height = 0f;
            normal = new Vector3(0f, 1f, 0f);
            Vector3 axis = shape.AxisUp;
            float offsetX = x - shape.Center.X;
            float offsetZ = z - shape.Center.Z;
            float quadratic = 1f - axis.Y * axis.Y;
            if (quadratic <= ParallelEpsilon) {
                return false;
            }

            float planarProjection = offsetX * axis.X + offsetZ * axis.Z;
            float linear = -2f * planarProjection * axis.Y;
            float constant = offsetX * offsetX + offsetZ * offsetZ - planarProjection * planarProjection - shape.Radius * shape.Radius;
            float discriminant = linear * linear - 4f * quadratic * constant;
            if (discriminant < 0f) {
                return false;
            }

            float rise = (-linear + MathF.Sqrt(discriminant)) / (2f * quadratic);
            float alongAxis = planarProjection + rise * axis.Y;
            if (MathF.Abs(alongAxis) > shape.HalfLength) {
                return false;
            }

            height = shape.Center.Y + rise;
            var surface = new Vector3(offsetX, rise, offsetZ);
            var toSurface = new Vector3(
                surface.X - alongAxis * axis.X,
                surface.Y - alongAxis * axis.Y,
                surface.Z - alongAxis * axis.Z);
            normal = new Vector3(toSurface.X / shape.Radius, toSurface.Y / shape.Radius, toSurface.Z / shape.Radius);
            return true;
        }

        /// <summary>How far a shape reaches above its own centre, used to start support probes clear of it.</summary>
        private static float VerticalReach(in CollisionShape shape) {
            if (shape.Type == CollisionShapeType.Sphere) {
                return shape.Radius;
            }

            if (shape.Type == CollisionShapeType.Capsule) {
                return MathF.Abs(shape.AxisUp.Y) * shape.HalfLength + shape.Radius;
            }

            return MathF.Abs(shape.AxisRight.Y) * shape.HalfExtents.X
                + MathF.Abs(shape.AxisUp.Y) * shape.HalfExtents.Y
                + MathF.Abs(shape.AxisForward.Y) * shape.HalfExtents.Z;
        }

        /// <summary>The endpoints of a capsule shape's inner segment, in world space.</summary>
        private static void GetShapeSegment(in CollisionShape shape, out Vector3 lower, out Vector3 upper) {
            Vector3 axis = shape.AxisUp;
            float halfLength = shape.HalfLength;
            lower = new Vector3(
                shape.Center.X - axis.X * halfLength,
                shape.Center.Y - axis.Y * halfLength,
                shape.Center.Z - axis.Z * halfLength);
            upper = new Vector3(
                shape.Center.X + axis.X * halfLength,
                shape.Center.Y + axis.Y * halfLength,
                shape.Center.Z + axis.Z * halfLength);
        }

        /// <summary>Where on a segment a point projects, clamped to the segment's ends.</summary>
        private static float ClosestParameterOnSegment(in Vector3 start, in Vector3 end, in Vector3 point) {
            Vector3 direction = Subtract(end, start);
            float lengthSquared = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
            if (lengthSquared <= DegenerateSquaredEpsilon) {
                return 0f;
            }

            Vector3 offset = Subtract(point, start);
            float projection = offset.X * direction.X + offset.Y * direction.Y + offset.Z * direction.Z;
            return Saturate(projection / lengthSquared);
        }

        /// <summary>
        /// The parameters of the closest pair of points on two segments. The degenerate cases — either
        /// segment collapsed to a point, the two parallel — are handled by falling back to a projection
        /// rather than by dividing by something that is nearly zero.
        /// </summary>
        private static void ClosestSegmentParameters(
            in Vector3 firstStart,
            in Vector3 firstEnd,
            in Vector3 secondStart,
            in Vector3 secondEnd,
            out float firstParameter,
            out float secondParameter) {
            Vector3 firstDirection = Subtract(firstEnd, firstStart);
            Vector3 secondDirection = Subtract(secondEnd, secondStart);
            Vector3 between = Subtract(firstStart, secondStart);
            float firstLengthSquared = Dot(firstDirection, firstDirection);
            float secondLengthSquared = Dot(secondDirection, secondDirection);
            float betweenOnSecond = Dot(secondDirection, between);
            if (firstLengthSquared <= DegenerateSquaredEpsilon && secondLengthSquared <= DegenerateSquaredEpsilon) {
                firstParameter = 0f;
                secondParameter = 0f;
                return;
            }

            if (firstLengthSquared <= DegenerateSquaredEpsilon) {
                firstParameter = 0f;
                secondParameter = Saturate(betweenOnSecond / secondLengthSquared);
                return;
            }

            float betweenOnFirst = Dot(firstDirection, between);
            if (secondLengthSquared <= DegenerateSquaredEpsilon) {
                firstParameter = Saturate(-betweenOnFirst / firstLengthSquared);
                secondParameter = 0f;
                return;
            }

            float directionDot = Dot(firstDirection, secondDirection);
            float denominator = firstLengthSquared * secondLengthSquared - directionDot * directionDot;
            firstParameter = denominator != 0f
                ? Saturate((directionDot * betweenOnSecond - betweenOnFirst * secondLengthSquared) / denominator)
                : 0f;
            secondParameter = (directionDot * firstParameter + betweenOnSecond) / secondLengthSquared;
            if (secondParameter < 0f) {
                secondParameter = 0f;
                firstParameter = Saturate(-betweenOnFirst / firstLengthSquared);
                return;
            }

            if (secondParameter > 1f) {
                secondParameter = 1f;
                firstParameter = Saturate((directionDot - betweenOnFirst) / firstLengthSquared);
            }
        }

        /// <summary>A world point expressed in a box's own frame, using its precomputed orthonormal basis.</summary>
        private static Vector3 ToBoxLocal(in CollisionShape shape, in Vector3 worldPoint) {
            Vector3 relative = Subtract(worldPoint, shape.Center);
            return new Vector3(Dot(relative, shape.AxisRight), Dot(relative, shape.AxisUp), Dot(relative, shape.AxisForward));
        }

        /// <summary>A box-local direction expressed back in world space.</summary>
        private static Vector3 ToWorldDirection(in CollisionShape shape, in Vector3 localDirection) {
            return new Vector3(
                shape.AxisRight.X * localDirection.X + shape.AxisUp.X * localDirection.Y + shape.AxisForward.X * localDirection.Z,
                shape.AxisRight.Y * localDirection.X + shape.AxisUp.Y * localDirection.Y + shape.AxisForward.Y * localDirection.Z,
                shape.AxisRight.Z * localDirection.X + shape.AxisUp.Z * localDirection.Y + shape.AxisForward.Z * localDirection.Z);
        }

        /// <summary>One of a box's three axes, signed — the outward normal of the face that axis owns.</summary>
        private static Vector3 ScaledAxis(in CollisionShape shape, int axisIndex, float sign) {
            if (axisIndex == 0) {
                return new Vector3(shape.AxisRight.X * sign, shape.AxisRight.Y * sign, shape.AxisRight.Z * sign);
            }

            if (axisIndex == 1) {
                return new Vector3(shape.AxisUp.X * sign, shape.AxisUp.Y * sign, shape.AxisUp.Z * sign);
            }

            return new Vector3(shape.AxisForward.X * sign, shape.AxisForward.Y * sign, shape.AxisForward.Z * sign);
        }

        /// <summary>Componentwise dot product, spelled out so the accumulation order is ours and not the JIT's.</summary>
        private static float Dot(in Vector3 left, in Vector3 right) {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        /// <summary>Componentwise subtraction, spelled out for the same reason as <see cref="Dot"/>.</summary>
        private static Vector3 Subtract(in Vector3 left, in Vector3 right) {
            return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        /// <summary>A point plus a scaled direction, spelled out for the same reason as <see cref="Dot"/>.</summary>
        private static Vector3 AddScaled(in Vector3 origin, in Vector3 direction, float scale) {
            return new Vector3(
                origin.X + direction.X * scale,
                origin.Y + direction.Y * scale,
                origin.Z + direction.Z * scale);
        }

        /// <summary>Clamps a value into a range using only the two permitted comparisons.</summary>
        private static float Clamp(float value, float minimum, float maximum) {
            return MathF.Min(MathF.Max(value, minimum), maximum);
        }

        /// <summary>Clamps a segment parameter into <c>[0, 1]</c>.</summary>
        private static float Saturate(float value) {
            return MathF.Min(MathF.Max(value, 0f), 1f);
        }

        /// <summary>The sign of a coordinate, with zero counted as positive so the result is never a null direction.</summary>
        private static float SignOf(float value) {
            return value < 0f ? -1f : 1f;
        }

        /// <summary>Insertion sort over the candidate parameters. Small, stable, and identical everywhere.</summary>
        private static void SortAscending(Span<float> values) {
            for (int index = 1; index < values.Length; index++) {
                float value = values[index];
                int scan = index - 1;
                while (scan >= 0 && values[scan] > value) {
                    values[scan + 1] = values[scan];
                    scan--;
                }

                values[scan + 1] = value;
            }
        }
    }
}
