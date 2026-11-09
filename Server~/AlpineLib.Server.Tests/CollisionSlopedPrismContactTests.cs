using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Capsule against sloped prism: agreement with a double-precision face-by-face reference, the interior tie order,
    /// seams between pieces sharing an edge, sliding off the rim and pieces too wide to bucket.
    /// </summary>
    public sealed class CollisionSlopedPrismContactTests {
        private const float TickSeconds = 1f / 30f;
        private const int RandomPoses = 10000;

        [Fact]
        public void RandomPosesMatchADoublePrecisionReference() {
            var random = new Random(4321);
            int contacts = 0;
            int topContacts = 0;
            for (int poseIndex = 0; poseIndex < RandomPoses; poseIndex++) {
                CollisionShape shape = RandomSlopedPrism(random);
                CapsulePose pose = RandomPose(random, shape);
                if (!CheckAgainstReference(in shape, in pose, out CollisionContact contact)) continue;

                contacts++;
                if (contact.Normal.Y > 0.05f && contact.Normal.Y < 0.999f) topContacts++;
            }

            Assert.True(contacts > RandomPoses / 4, "Only " + contacts + " contacts were exercised.");
            Assert.True(topContacts > RandomPoses / 20, "Only " + topContacts + " slanted contacts were exercised.");
        }

        [Fact]
        public void OverTheTopTheNormalIsThePlaneNormal() {
            CollisionShape shape = CollisionShape.MakeSlopedPrism(Vector3.Zero, -3f, 1f, 0.7f, 0f, Square(3f));
            var pose = new CapsulePose(new Vector3(0f, 1.0f, 0f), 0.35f, 1.1f);

            Assert.True(CollisionResolver.TryGetContact(in pose, in shape, out CollisionContact contact));

            float length = MathF.Sqrt(1f + 0.49f);
            Assert.Equal(-0.7f / length, contact.Normal.X, 6);
            Assert.Equal(1f / length, contact.Normal.Y, 6);
            Assert.Equal(0f, contact.Normal.Z);
            Assert.Equal(0.35f - 0.35f / length, contact.Depth, 5);
        }

        [Fact]
        public void PastTheLowRimTheNearestPointIsStillOnTheTopFace() {
            // Downhill of the low edge, above the plane's extension: the projection runs back uphill onto the face.
            CollisionShape shape = CollisionShape.MakeSlopedPrism(Vector3.Zero, -3f, 0f, 1f, 0f, Square(2f));
            var pose = new CapsulePose(new Vector3(-2.1f, -2.05f, 0f), 0.35f, 1.1f);

            Assert.True(CollisionResolver.TryGetContact(in pose, in shape, out CollisionContact contact));

            Assert.Equal(-MathF.Sqrt(0.5f), contact.Normal.X, 5);
            Assert.Equal(MathF.Sqrt(0.5f), contact.Normal.Y, 5);
        }

        [Fact]
        public void InteriorTiesGoUpThenSideThenDown() {
            CollisionShape flat = CollisionShape.MakeSlopedPrism(Vector3.Zero, -1f, 1f, 0f, 0f, Square(1f));
            var upOrSide = new CapsulePose(new Vector3(0.5f, 0.25f, 0f), 0.25f, 2f);
            var sideOrDown = new CapsulePose(new Vector3(0.5f, -0.75f, 0f), 0.25f, 0.5f);

            Assert.True(CollisionResolver.TryGetContact(in upOrSide, in flat, out CollisionContact up));
            Assert.True(CollisionResolver.TryGetContact(in sideOrDown, in flat, out CollisionContact side));

            Assert.Equal(new Vector3(0f, 1f, 0f), up.Normal);
            Assert.Equal(0.75f, up.Depth);
            Assert.Equal(new Vector3(1f, 0f, 0f), side.Normal);
            Assert.Equal(0.75f, side.Depth);

            CollisionShape sloped = CollisionShape.MakeSlopedPrism(Vector3.Zero, -2f, 1f, 0.75f, 0f, Square(2f));
            var buried = new CapsulePose(new Vector3(0f, 0.5f, 0f), 0.25f, 1f);
            Assert.True(CollisionResolver.TryGetContact(in buried, in sloped, out CollisionContact slanted));
            Assert.Equal(-0.6f, slanted.Normal.X, 6);
            Assert.Equal(0.8f, slanted.Normal.Y, 6);
            Assert.Equal(0.25f + 0.25f * 0.8f, slanted.Depth, 6);
        }

        [Fact]
        public void PiecesSharingAnEdgeWalkLikeOneRamp() {
            CollisionShape[] split = { CollisionShape.MakePlane(0f), Ramp(new[] { new Vector2(-4f, -4f), new Vector2(4f, -4f), new Vector2(4f, 4f) }), Ramp(new[] { new Vector2(-4f, -4f), new Vector2(4f, 4f), new Vector2(-4f, 4f) }) };
            CollisionShape[] whole = { CollisionShape.MakePlane(0f), Ramp(Square(4f)) };
            foreach (Vector2 direction in new[] { new Vector2(1f, 0.1f), new Vector2(0.2f, 1f), new Vector2(-1f, 0.4f) }) {
                List<PawnState> overSeam = Walk(World(split), new Vector3(-0.5f * direction.X, 1f, -0.5f * direction.Y), direction, ShippedProfile(), 45);
                List<PawnState> overRamp = Walk(World(whole), new Vector3(-0.5f * direction.X, 1f, -0.5f * direction.Y), direction, ShippedProfile(), 45);
                for (int tick = 0; tick < overSeam.Count; tick++) {
                    Assert.True(overSeam[tick].IsGrounded);
                    Assert.True(Vector3.Distance(overRamp[tick].Position, overSeam[tick].Position) < 1e-4f, "Tick " + tick + ": " + overSeam[tick].Position + " vs " + overRamp[tick].Position);
                    Assert.Equal(overRamp[tick].Velocity.Y, overSeam[tick].Velocity.Y);
                }
            }

            CollisionWorld seamWorld = World(split);
            var collected = new CollisionContact[CollisionWorld.MaxContacts];
            for (int step = 0; step <= 40; step++) {
                float along = -3.5f + step * 0.175f;
                float top = 1f + 0.4f * along + 0.25f * along;
                var falling = new CapsulePose(new Vector3(along, top - 0.05f, along), 0.35f, 1.1f);
                int count = seamWorld.CollectContacts(in falling, 1u, collected);
                Assert.True(count > 0);
                for (int index = 0; index < count; index++) Assert.True(collected[index].Normal.Y > 0.8f, "Seam normal " + collected[index].Normal);
            }
        }

        [Fact]
        public void AFallingPawnSlidesOffTheRim() {
            CollisionShape[] shapes = { CollisionShape.MakePlane(0f), CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 1.5f, 0.3f, 0f, Square(2f)) };
            CollisionShape slope = shapes[1];
            var straddling = new CapsulePose(new Vector3(2.2f, 2.0f, 0f), 0.35f, 1.1f);
            Assert.True(CollisionResolver.TryGetContact(in straddling, in slope, out CollisionContact rim));
            Assert.True(rim.Normal.X > 0.3f && rim.Normal.Y > 0.3f, "Rim normal " + rim.Normal);

            var state = new PawnState(new Vector3(2.2f, 2.1f, 0f), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, false));
            CollisionWorld world = World(shapes);
            MovementProfile profile = ShippedProfile();
            for (int tick = 1; tick <= 60; tick++) {
                var input = new PawnInput((uint)tick, Vector2.Zero, WireLocomotion.Walk, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, (uint)tick, TickSeconds);
            }

            Assert.True(state.IsGrounded);
            Assert.Equal(0f, state.Position.Y);
            Assert.True(state.Position.X >= 2.35f - 1e-4f);
        }

        [Fact]
        public void PiecesTooWideToBucketStillCollideAndSupport() {
            CollisionWorld world = World(Array.Empty<CollisionShape>());
            world.RemoveGroup(CollisionWorld.SceneGroupKey);
            world.AddGroup(1ul, new[] { CollisionShape.MakeSlopedPrism(Vector3.Zero, -50f, 0f, 0.25f, -0.125f, Square(100f)) }, new Vector3(10f, 0f, 0f));
            var collected = new CollisionContact[CollisionWorld.MaxContacts];

            float top = 0.25f * 85f + 0.125f * 90f;
            var pose = new CapsulePose(new Vector3(95f, top - 0.1f, -90f), 0.35f, 1.1f);
            Assert.Equal(1, world.CollectContacts(in pose, 1u, collected));
            Assert.True(collected[0].Normal.Y > 0.9f && collected[0].Normal.X < 0f && collected[0].Normal.Z > 0f);
            Assert.True(world.TryGetSupport(95f, -90f, top + 1f, top - 1f, 1u, out SupportHit hit));
            Assert.Equal(top, hit.Height, 3);
        }

        [Fact]
        public void TheShippedProfileWalksUpThirtyFiveDegreesWithoutCorrectionAndStopsAtTheWall() {
            const float Slope = 0.70020754f;
            CollisionShape ramp = CollisionShape.MakeSlopedPrism(Vector3.Zero, -1f, 0f, Slope, 0f, new[] { new Vector2(0f, -3f), new Vector2(30f, -3f), new Vector2(30f, 3f), new Vector2(0f, 3f) });
            CollisionShape wall = CollisionShape.MakePrism(new Vector3(31f, 12f, 0f), 13f, Square(1f));
            CollisionWorld world = World(new[] { CollisionShape.MakePlane(0f), ramp, wall });
            MovementProfile profile = ShippedProfile();
            List<PawnState> states = Walk(world, new Vector3(-2f, 0f, 0f), new Vector2(1f, 0f), profile, 600);

            var collected = new CollisionContact[CollisionWorld.MaxContacts];
            int corrections = 0;
            for (int tick = 0; tick < states.Count; tick++) {
                PawnState state = states[tick];
                Assert.True(state.IsGrounded, "Airborne at tick " + tick);
                if (state.Position.X < 0f || state.Position.X > 29f) continue;

                Assert.Equal(ramp.SlopedTopHeight(state.Position.X, state.Position.Z), state.Position.Y, 4);
                var raised = new CapsulePose(state.Position + new Vector3(0f, profile.StepOffset, 0f), profile.CapsuleRadius, profile.CapsuleHeight - profile.StepOffset);
                corrections += world.CollectContacts(in raised, 1u, collected);
                if (tick > 0 && states[tick - 1].Position.X >= 0f) Assert.Equal(states[tick - 1].Position.X + profile.WalkSpeed * TickSeconds, state.Position.X, 4);
            }

            PawnState last = states[states.Count - 1];
            Assert.Equal(0, corrections);
            Assert.InRange(last.Position.X, 30f - profile.CapsuleRadius - 0.01f, 30f - profile.CapsuleRadius + 1e-4f);
            Assert.Equal(ramp.SlopedTopHeight(last.Position.X, 0f), last.Position.Y, 3);
        }

        [Fact]
        public void A40DegreeProfileCannotClimbAFortyFiveDegreePiece() {
            CollisionShape ramp = CollisionShape.MakeSlopedPrism(Vector3.Zero, -1f, 0f, 1f, 0f, new[] { new Vector2(0f, -3f), new Vector2(12f, -3f), new Vector2(12f, 3f), new Vector2(0f, 3f) });
            CollisionWorld world = World(new[] { CollisionShape.MakePlane(0f), ramp });
            MovementProfile profile = ShippedProfile();
            profile.SlopeLimitDegrees = 40f;

            List<PawnState> states = Walk(world, new Vector3(-2f, 0f, 0f), new Vector2(1f, 0f), profile, 240);

            float highest = 0f;
            foreach (PawnState state in states) highest = MathF.Max(highest, state.Position.Y);
            Assert.True(highest < 0.6f, "Climbed to " + highest);
            Assert.True(states[states.Count - 1].Position.X < 1f);
        }

        // Checks one pose; returns true when the reference says the two overlap.
        private static bool CheckAgainstReference(in CollisionShape shape, in CapsulePose pose, out CollisionContact contact) {
            bool touched = CollisionResolver.TryGetContact(in pose, in shape, out contact);
            ReferenceContact(in shape, in pose, out double depth, out Vector3 normal, out bool normalIsDefined);
            if (depth <= -1e-4) {
                Assert.False(touched, "Touched at reference depth " + depth);
                return false;
            }

            if (depth < 1e-4) return false;

            Assert.True(touched, "Missed at reference depth " + depth);
            Assert.InRange(contact.Depth, depth - 1e-4, depth + 1e-4);
            if (normalIsDefined) Assert.True(Vector3.Distance(normal, contact.Normal) <= 1e-3f, "Normal " + contact.Normal + " vs " + normal);
            return true;
        }

        /// <summary>
        /// Reference penetration (negative when apart): ternary search along the segment of the point-to-solid distance,
        /// convex for a convex solid, measured face by face; for an overlapping segment the shortest of the escapes.
        /// </summary>
        private static void ReferenceContact(in CollisionShape shape, in CapsulePose pose, out double depth, out Vector3 normal, out bool normalIsDefined) {
            var solid = new ReferenceSolid(shape);
            double x = pose.FootPosition.X, z = pose.FootPosition.Z;
            double low = pose.SegmentBottom.Y, high = pose.SegmentTop.Y;
            double lower = 0.0, upper = 1.0;
            for (int iteration = 0; iteration < 60; iteration++) {
                double first = lower + (upper - lower) / 3.0, second = upper - (upper - lower) / 3.0;
                if (solid.Distance(x, low + (high - low) * first, z) <= solid.Distance(x, low + (high - low) * second, z)) upper = second;
                else lower = first;
            }
            double bestY = low + (high - low) * (lower + upper) * 0.5;
            double distance = solid.Distance(x, bestY, z);
            if (distance > 1e-6) {
                depth = pose.Radius - distance;
                Vector3Double closest = solid.Closest(x, bestY, z);
                normal = new Vector3((float)((x - closest.X) / distance), (float)((bestY - closest.Y) / distance), (float)((z - closest.Z) / distance));
                normalIsDefined = distance > 0.01;
                return;
            }

            solid.InteriorEscape(x, z, low, high, out double escape, out normal, out normalIsDefined);
            depth = escape + pose.Radius;
        }

        // A convex prism with a sloped top, as double faces.
        private sealed class ReferenceSolid {
            private readonly CollisionShape _shape;
            private readonly List<Vector3Double[]> _faces = new List<Vector3Double[]>();
            private readonly List<Vector3Double> _normals = new List<Vector3Double>();
            private readonly double[] _planX;
            private readonly double[] _planZ;

            public ReferenceSolid(CollisionShape shape) {
                _shape = shape;
                int count = shape.Plan.Count;
                _planX = new double[count];
                _planZ = new double[count];
                var top = new Vector3Double[count];
                var bottom = new Vector3Double[count];
                for (int index = 0; index < count; index++) {
                    shape.Plan.GetVertex(index, out float x, out float z);
                    _planX[index] = shape.Center.X + (double)x;
                    _planZ[index] = shape.Center.Z + (double)z;
                    top[index] = new Vector3Double(_planX[index], Top(_planX[index], _planZ[index]), _planZ[index]);
                    bottom[count - 1 - index] = new Vector3Double(_planX[index], shape.BottomY, _planZ[index]);
                }
                _faces.Add(top);
                _faces.Add(bottom);
                for (int index = 0; index < count; index++) {
                    int next = (index + 1) % count;
                    _faces.Add(new[] {
                        new Vector3Double(_planX[index], shape.BottomY, _planZ[index]),
                        new Vector3Double(_planX[index], top[index].Y, _planZ[index]),
                        new Vector3Double(_planX[next], top[next].Y, _planZ[next]),
                        new Vector3Double(_planX[next], shape.BottomY, _planZ[next]),
                    });
                }
                foreach (Vector3Double[] face in _faces) _normals.Add(Newell(face));
            }

            public double Top(double x, double z) {
                return _shape.TopY0 + _shape.TopSlopeX * (x - _shape.Center.X) + _shape.TopSlopeZ * (z - _shape.Center.Z);
            }

            public double Distance(double x, double y, double z) {
                Vector3Double closest = Closest(x, y, z);
                return Math.Sqrt((x - closest.X) * (x - closest.X) + (y - closest.Y) * (y - closest.Y) + (z - closest.Z) * (z - closest.Z));
            }

            public Vector3Double Closest(double x, double y, double z) {
                var point = new Vector3Double(x, y, z);
                if (MaxSigned(x, z, out int _) <= 0.0 && y >= _shape.BottomY && y <= Top(x, z)) return point;

                Vector3Double best = point;
                double bestSquared = double.PositiveInfinity;
                for (int face = 0; face < _faces.Count; face++) {
                    Vector3Double candidate = ClosestOnPolygon(_faces[face], _normals[face], point);
                    double squared = (candidate - point).LengthSquared;
                    if (squared >= bestSquared) continue;

                    bestSquared = squared;
                    best = candidate;
                }
                return best;
            }

            public void InteriorEscape(double x, double z, double low, double high, out double escape, out Vector3 normal, out bool normalIsDefined) {
                double length = Math.Sqrt(1.0 + (double)_shape.TopSlopeX * _shape.TopSlopeX + (double)_shape.TopSlopeZ * _shape.TopSlopeZ);
                double up = (Top(x, z) - low) / length;
                double side = -MaxSigned(x, z, out int edge);
                double down = high - _shape.BottomY;
                var escapes = new List<double> { up, side, down };
                escapes.Sort();
                escape = escapes[0];
                normalIsDefined = escapes[1] - escapes[0] > 1e-3;
                int next = (edge + 1) % _planX.Length;
                double edgeX = _planX[next] - _planX[edge], edgeZ = _planZ[next] - _planZ[edge];
                double edgeLength = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
                normal = escapes[0] == up ? new Vector3((float)(-_shape.TopSlopeX / length), (float)(1.0 / length), (float)(-_shape.TopSlopeZ / length))
                    : escapes[0] == down ? -Vector3.UnitY : new Vector3((float)(edgeZ / edgeLength), 0f, (float)(-edgeX / edgeLength));
            }

            private double MaxSigned(double x, double z, out int edge) {
                double largest = double.NegativeInfinity;
                edge = 0;
                for (int index = 0; index < _planX.Length; index++) {
                    int next = (index + 1) % _planX.Length;
                    double edgeX = _planX[next] - _planX[index], edgeZ = _planZ[next] - _planZ[index];
                    double signed = ((x - _planX[index]) * edgeZ - (z - _planZ[index]) * edgeX) / Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
                    if (signed <= largest) continue;

                    largest = signed;
                    edge = index;
                }
                return largest;
            }

            // Closest point of a planar convex polygon: the plane projection when it falls inside, else the nearest edge point.
            private static Vector3Double ClosestOnPolygon(Vector3Double[] polygon, Vector3Double normal, Vector3Double point) {
                Vector3Double projected = point - normal * (point - polygon[0]).Dot(normal);
                bool inside = true;
                for (int index = 0; index < polygon.Length; index++) {
                    Vector3Double start = polygon[index], end = polygon[(index + 1) % polygon.Length];
                    if ((end - start).Cross(projected - start).Dot(normal) < 0.0) inside = false;
                }
                if (inside) return projected;

                Vector3Double best = polygon[0];
                double bestSquared = double.PositiveInfinity;
                for (int index = 0; index < polygon.Length; index++) {
                    Vector3Double start = polygon[index], edge = polygon[(index + 1) % polygon.Length] - start;
                    double along = Math.Max(0.0, Math.Min(1.0, (point - start).Dot(edge) / edge.LengthSquared));
                    Vector3Double candidate = start + edge * along;
                    double squared = (candidate - point).LengthSquared;
                    if (squared >= bestSquared) continue;

                    bestSquared = squared;
                    best = candidate;
                }
                return best;
            }

            private static Vector3Double Newell(Vector3Double[] polygon) {
                double x = 0.0, y = 0.0, z = 0.0;
                for (int index = 0; index < polygon.Length; index++) {
                    Vector3Double current = polygon[index], next = polygon[(index + 1) % polygon.Length];
                    x += (current.Y - next.Y) * (current.Z + next.Z);
                    y += (current.Z - next.Z) * (current.X + next.X);
                    z += (current.X - next.X) * (current.Y + next.Y);
                }
                double length = Math.Sqrt(x * x + y * y + z * z);
                return new Vector3Double(x / length, y / length, z / length);
            }
        }

        private readonly struct Vector3Double {
            public Vector3Double(double x, double y, double z) {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double LengthSquared => X * X + Y * Y + Z * Z;

            public double Dot(Vector3Double other) => X * other.X + Y * other.Y + Z * other.Z;

            public Vector3Double Cross(Vector3Double other) => new Vector3Double(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);

            public static Vector3Double operator -(Vector3Double left, Vector3Double right) => new Vector3Double(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

            public static Vector3Double operator +(Vector3Double left, Vector3Double right) => new Vector3Double(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

            public static Vector3Double operator *(Vector3Double vector, double scale) => new Vector3Double(vector.X * scale, vector.Y * scale, vector.Z * scale);
        }

        // A convex plan of 3–16 points on a turned ellipse with a top 0–45° steep in a random direction.
        internal static CollisionShape RandomSlopedPrism(Random random) {
            int count = 3 + random.Next(14);
            double radiusA = 0.3 + random.NextDouble() * 2.7, radiusB = 0.3 + random.NextDouble() * 2.7, turn = random.NextDouble() * Math.PI * 2.0;
            var offsets = new Vector2[count];
            for (int index = 0; index < count; index++) {
                double angle = (index + (random.NextDouble() - 0.5) * 0.6) * 2.0 * Math.PI / count;
                double localX = radiusA * Math.Cos(angle), localZ = radiusB * Math.Sin(angle);
                offsets[index] = new Vector2((float)(localX * Math.Cos(turn) - localZ * Math.Sin(turn)), (float)(localX * Math.Sin(turn) + localZ * Math.Cos(turn)));
            }
            double gradient = Math.Tan(random.NextDouble() * Math.PI / 4.0) * 0.9999, heading = random.NextDouble() * Math.PI * 2.0;
            float slopeX = (float)(gradient * Math.Cos(heading)), slopeZ = (float)(gradient * Math.Sin(heading));
            float bottom = (float)(random.NextDouble() * 4.0 - 2.0);
            float topY0 = (float)(bottom + 0.2 + random.NextDouble() * 1.8 + gradient * Math.Max(radiusA, radiusB) * 1.2);
            var center = new Vector3((float)(random.NextDouble() * 10.0 - 5.0), 0f, (float)(random.NextDouble() * 10.0 - 5.0));
            return CollisionShape.MakeSlopedPrism(center, bottom, topY0, slopeX, slopeZ, offsets);
        }

        private static CapsulePose RandomPose(Random random, in CollisionShape shape) {
            float radius = (float)(0.2 + random.NextDouble() * 0.4);
            float height = radius * 2f + (float)(random.NextDouble() * 2.0);
            double reach = shape.Radius + radius;
            double bottom = shape.BottomY - height - 0.3, top = shape.Center.Y + shape.HalfExtents.Y + 0.3;
            var foot = new Vector3(
                shape.Center.X + (float)((random.NextDouble() * 2.0 - 1.0) * reach),
                (float)(bottom + random.NextDouble() * (top - bottom)),
                shape.Center.Z + (float)((random.NextDouble() * 2.0 - 1.0) * reach));
            return new CapsulePose(foot, radius, height);
        }

        // The project's shipped pawn: 0.35 × 1.1 m capsule, 0.3 m step, 45° slope limit.
        internal static MovementProfile ShippedProfile() {
            return new MovementProfile { CapsuleRadius = 0.35f, CapsuleHeight = 1.1f, StepOffset = 0.3f, SlopeLimitDegrees = 45f };
        }

        private static Vector2[] Square(float half) {
            return new[] { new Vector2(-half, -half), new Vector2(half, -half), new Vector2(half, half), new Vector2(-half, half) };
        }

        // One plane, y = 1 + 0.4·x + 0.25·z, over the given plan around the origin, bottom at −3.
        private static CollisionShape Ramp(Vector2[] plan) {
            return CollisionShape.MakeSlopedPrism(Vector3.Zero, -3f, 1f, 0.4f, 0.25f, plan);
        }

        private static CollisionWorld World(CollisionShape[] shapes) {
            return new CollisionWorld(new SceneGeometry("SlopedPrisms", 1u, shapes, Array.Empty<MoverDefinition>()), TickSeconds);
        }

        // Steps a grounded pawn along a fixed input.
        private static List<PawnState> Walk(CollisionWorld world, Vector3 start, Vector2 direction, MovementProfile profile, int ticks) {
            var state = new PawnState(start, 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var states = new List<PawnState>();
            for (int tick = 0; tick < ticks; tick++) {
                var input = new PawnInput((uint)tick + 1u, direction, WireLocomotion.Walk, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, (uint)tick + 1u, TickSeconds);
                states.Add(state);
            }
            return states;
        }
    }
}
