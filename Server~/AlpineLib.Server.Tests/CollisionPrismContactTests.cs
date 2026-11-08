using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Diagnostics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Capsule against prism: agreement with a double-precision reference, the rim, corner and interior
    /// rules, seams between stacked prisms under the real motor, and the bits of Mono-sensitive golden cases.
    /// </summary>
    public sealed class CollisionPrismContactTests {
        private const float TickSeconds = 1f / 30f;
        private const int RandomPoses = 10000;

        [Fact]
        public void RandomPosesMatchADoublePrecisionReference() {
            var random = new Random(1234);
            int contacts = 0;
            for (int poseIndex = 0; poseIndex < RandomPoses; poseIndex++) {
                CollisionShape prism = RandomPrism(random);
                CapsulePose pose = RandomPose(random, prism);
                if (CheckAgainstReference(in prism, in pose)) contacts++;
            }

            Assert.True(contacts > RandomPoses / 4, "Only " + contacts + " contacts were exercised.");
        }

        [Fact]
        public void PastTheRimTheNormalLeansUpAndOut() {
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(0f, 0.5f, 0f), 0.5f, Square(1f));
            var pose = new CapsulePose(new Vector3(1.2f, 0.9f, 0f), 0.35f, 1.1f);

            Assert.True(CollisionResolver.TryGetContact(in pose, in prism, out CollisionContact contact));

            Assert.True(contact.Normal.X > 0.5f && contact.Normal.Y > 0.5f);
            Assert.Equal(0f, contact.Normal.Z);
            Assert.Equal(0.35f - MathF.Sqrt(0.2f * 0.2f + 0.25f * 0.25f), contact.Depth, 5);
        }

        [Fact]
        public void ACornerPushesDiagonally() {
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(0f, 0.5f, 0f), 0.5f, Square(1f));
            var pose = new CapsulePose(new Vector3(1.2f, 0.2f, 1.2f), 0.35f, 1.1f);

            Assert.True(CollisionResolver.TryGetContact(in pose, in prism, out CollisionContact contact));

            Assert.Equal(MathF.Sqrt(0.5f), contact.Normal.X, 5);
            Assert.Equal(0f, contact.Normal.Y);
            Assert.Equal(MathF.Sqrt(0.5f), contact.Normal.Z, 5);
            Assert.Equal(0.35f - MathF.Sqrt(0.08f), contact.Depth, 5);
        }

        [Fact]
        public void InteriorTiesGoUpThenSideThenDown() {
            CollisionShape prism = CollisionShape.MakePrism(Vector3.Zero, 1f, Square(1f));
            var upOrSide = new CapsulePose(new Vector3(0.5f, 0.25f, 0f), 0.25f, 2f);
            var sideOrDown = new CapsulePose(new Vector3(0.5f, -0.75f, 0f), 0.25f, 0.5f);
            var twoEdges = new CapsulePose(new Vector3(0.5f, -0.75f, 0.5f), 0.25f, 0.5f);

            Assert.True(CollisionResolver.TryGetContact(in upOrSide, in prism, out CollisionContact up));
            Assert.True(CollisionResolver.TryGetContact(in sideOrDown, in prism, out CollisionContact side));
            Assert.True(CollisionResolver.TryGetContact(in twoEdges, in prism, out CollisionContact lowerEdge));

            Assert.Equal(new Vector3(0f, 1f, 0f), up.Normal);
            Assert.Equal(0.75f, up.Depth);
            Assert.Equal(new Vector3(1f, 0f, 0f), side.Normal);
            Assert.Equal(0.75f, side.Depth);
            Assert.Equal(new Vector3(1f, 0f, 0f), lowerEdge.Normal);
        }

        [Fact]
        public void AStackedSeamSlidesLikeOneWall() {
            CollisionShape[] stacked = { CollisionShape.MakePlane(0f), Prism(0f, 1f), Prism(1f, 2.25f) };
            CollisionShape[] merged = { CollisionShape.MakePlane(0f), Prism(0f, 2.25f) };
            var collected = new CollisionContact[CollisionWorld.MaxContacts];
            CollisionWorld stackedWorld = World(stacked);
            for (int step = 0; step <= 40; step++) {
                var pose = new CapsulePose(new Vector3(2.3f, step * 0.0275f, -1.5f + step * 0.075f), 0.35f, 1.1f);
                int count = stackedWorld.CollectContacts(in pose, 1u, collected);
                Assert.True(count > 0);
                Assert.Equal(0f, Deepest(collected, count).Normal.Y);
            }

            foreach (bool jump in new[] { false, true }) {
                List<PawnState> overSeam = Walk(World(stacked), new Vector3(2.36f, 0f, -1.5f), new Vector2(-0.3f, 0.95f), jump, 45);
                List<PawnState> overWall = Walk(World(merged), new Vector3(2.36f, 0f, -1.5f), new Vector2(-0.3f, 0.95f), jump, 45);
                for (int tick = 0; tick < overSeam.Count; tick++) {
                    Assert.Equal(overWall[tick].Position, overSeam[tick].Position);
                    Assert.Equal(overWall[tick].Velocity, overSeam[tick].Velocity);
                }

                Assert.True(overSeam[overSeam.Count - 1].Position.Z > 1f);
                Assert.True(overSeam[overSeam.Count - 1].Position.X >= 2.34f);
            }
        }

        // A grounded pawn sinks one tick of gravity (20 / 30² ≈ 0.022 m) before its feet are raised by the
        // step offset, so the motor's real step is about 0.278 m; the fitter's feet cap is 0.2678. Above the real
        // step a prism blocks exactly like a box.
        [Theory]
        [InlineData(0.15f, true)]
        [InlineData(0.25f, true)]
        [InlineData(0.2678f, true)]
        [InlineData(0.275f, true)]
        [InlineData(0.29f, false)]
        public void TheMotorStepsOntoAFootBandLikeABox(float top, bool climbs) {
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(1f, top * 0.5f, 0f), top * 0.5f, Square(1f));
            CollisionShape box = CollisionShape.MakeBox(new Vector3(1f, top * 0.5f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(1f, top * 0.5f, 1f));

            PawnState onPrism = Walk(World(new[] { CollisionShape.MakePlane(0f), prism }), new Vector3(-1f, 0f, 0f), new Vector2(1f, 0f), false, 30)[29];
            PawnState onBox = Walk(World(new[] { CollisionShape.MakePlane(0f), box }), new Vector3(-1f, 0f, 0f), new Vector2(1f, 0f), false, 30)[29];

            Assert.True(onPrism.IsGrounded);
            Assert.Equal(onBox.Position.X, onPrism.Position.X, 5);
            Assert.Equal(onBox.Position.Y, onPrism.Position.Y, 5);
            Assert.Equal(climbs ? top : 0f, onPrism.Position.Y, 5);
            Assert.Equal(climbs, onPrism.Position.X > 0.5f);
        }

        [Fact]
        public void TheWorldCollectsPrismsIncludingOnesTooWideToBucket() {
            CollisionWorld world = World(Array.Empty<CollisionShape>());
            world.RemoveGroup(CollisionWorld.SceneGroupKey);
            world.AddGroup(1ul, new[] { CollisionShape.MakePrism(new Vector3(0f, 0.5f, 0f), 0.5f, Square(1f)) }, new Vector3(30f, 0f, -20f));
            world.AddGroup(2ul, new[] { CollisionShape.MakePrism(new Vector3(0f, -1f, 0f), 1f, Square(100f)) }, Vector3.Zero);
            var collected = new CollisionContact[CollisionWorld.MaxContacts];

            var nearSmall = new CapsulePose(new Vector3(31.2f, 0.1f, -20f), 0.35f, 1.1f);
            int count = world.CollectContacts(in nearSmall, 1u, collected);
            Assert.True(count >= 1);
            Assert.Contains(collected[..count], (contact) => contact.Normal.X > 0.99f);

            var onWide = new CapsulePose(new Vector3(95f, -0.1f, -95f), 0.35f, 1.1f);
            Assert.Equal(1, world.CollectContacts(in onWide, 1u, collected));
            Assert.Equal(new Vector3(0f, 1f, 0f), collected[0].Normal);
            Assert.True(world.TryGetSupport(-99f, 99f, 1f, -1f, 1u, out SupportHit hit));
            Assert.Equal(0f, hit.Height);
            Assert.True(world.TryGetSupport(30.5f, -20.5f, 2f, 0.5f, 1u, out SupportHit small));
            Assert.Equal(1f, small.Height);
        }

        // Prism golden cases that need every operation narrowed: sideways and diagonal normals and depths outside the
        // plan. Mono with float32 off must give these CoreCLR bits, as it must for MonoParityGoldens.PrismHash.
        [Theory]
        [InlineData(35, 1, 1056835051u, 0u, 1063142703u, 1020155136u)]
        [InlineData(38, 0, 1024470614u, 3212447774u, 1045972311u, 1048277254u)]
        [InlineData(49, 3, 1062191287u, 0u, 3205862653u, 1026157088u)]
        [InlineData(119, 1, 1056835055u, 0u, 1063142702u, 1026462808u)]
        [InlineData(748, 1, 3204318687u, 0u, 3210626354u, 1052006138u)]
        public void MonoSensitiveGoldenCasesKeepTheirBits(int index, int prism, uint normalX, uint normalY, uint normalZ, uint depth) {
            CapsulePose pose = MonoParityGoldens.Pose(index);
            CollisionShape shape = MonoParityGoldens.Prisms()[prism];

            Assert.True(CollisionResolver.TryGetContact(in pose, in shape, out CollisionContact contact));

            Assert.Equal(normalX, Bits(contact.Normal.X));
            Assert.Equal(normalY, Bits(contact.Normal.Y));
            Assert.Equal(normalZ, Bits(contact.Normal.Z));
            Assert.Equal(depth, Bits(contact.Depth));
        }

        private static uint Bits(float value) {
            return BitConverter.SingleToUInt32Bits(value);
        }

        // Checks one pose; returns true when the reference says the two overlap.
        private static bool CheckAgainstReference(in CollisionShape prism, in CapsulePose pose) {
            bool touched = CollisionResolver.TryGetContact(in pose, in prism, out CollisionContact contact);
            ReferenceContact(in prism, in pose, out double depth, out Vector3 normal, out bool normalIsDefined);
            if (depth <= -1e-4) {
                Assert.False(touched);
                return false;
            }

            if (depth < 1e-4) return false;

            Assert.True(touched);
            Assert.InRange(contact.Depth, depth - 1e-4, depth + 1e-4);
            if (normalIsDefined) Assert.True(Vector3.Distance(normal, contact.Normal) <= 1e-3f, "Normal " + contact.Normal + " vs " + normal);
            return true;
        }

        /// <summary>
        /// Reference penetration (negative when apart) by ternary search of the segment-to-solid distance,
        /// which is convex along the segment, and for an overlapping segment the shortest of the escapes.
        /// </summary>
        private static void ReferenceContact(in CollisionShape prism, in CapsulePose pose, out double depth, out Vector3 normal, out bool normalIsDefined) {
            double low = pose.SegmentBottom.Y, high = pose.SegmentTop.Y;
            double lower = 0.0, upper = 1.0;
            for (int iteration = 0; iteration < 200; iteration++) {
                double first = lower + (upper - lower) / 3.0, second = upper - (upper - lower) / 3.0;
                if (SolidDistance(prism, pose, low + (high - low) * first) <= SolidDistance(prism, pose, low + (high - low) * second)) upper = second;
                else lower = first;
            }
            double bestY = low + (high - low) * (lower + upper) * 0.5;
            double distance = SolidDistance(prism, pose, bestY);
            if (distance > 1e-6) {
                depth = pose.Radius - distance;
                ClosestOnSolid(prism, pose.FootPosition.X, bestY, pose.FootPosition.Z, out double closestX, out double closestY, out double closestZ);
                normal = new Vector3((float)((pose.FootPosition.X - closestX) / distance), (float)((bestY - closestY) / distance), (float)((pose.FootPosition.Z - closestZ) / distance));
                normalIsDefined = distance > 0.01;
                return;
            }

            InteriorEscape(prism, pose, out depth, out normal, out normalIsDefined);
        }

        private static double SolidDistance(in CollisionShape prism, in CapsulePose pose, double y) {
            ClosestOnSolid(prism, pose.FootPosition.X, y, pose.FootPosition.Z, out double closestX, out double closestY, out double closestZ);
            double gapX = pose.FootPosition.X - closestX, gapY = y - closestY, gapZ = pose.FootPosition.Z - closestZ;
            return Math.Sqrt(gapX * gapX + gapY * gapY + gapZ * gapZ);
        }

        private static void ClosestOnSolid(in CollisionShape prism, double x, double y, double z, out double closestX, out double closestY, out double closestZ) {
            double planX = x - prism.Center.X, planZ = z - prism.Center.Z;
            closestY = Math.Max(prism.Center.Y - (double)prism.HalfExtents.Y, Math.Min(prism.Center.Y + (double)prism.HalfExtents.Y, y));
            closestX = x;
            closestZ = z;
            if (MaxSignedEdge(prism, planX, planZ, out int _) <= 0.0) return;

            double best = double.PositiveInfinity;
            for (int index = 0; index < prism.Plan.Count; index++) {
                Edge(prism, index, out double startX, out double startZ, out double edgeX, out double edgeZ);
                double along = Math.Max(0.0, Math.Min(1.0, ((planX - startX) * edgeX + (planZ - startZ) * edgeZ) / (edgeX * edgeX + edgeZ * edgeZ)));
                double pointX = startX + edgeX * along, pointZ = startZ + edgeZ * along;
                double squared = (planX - pointX) * (planX - pointX) + (planZ - pointZ) * (planZ - pointZ);
                if (squared >= best) continue;

                best = squared;
                closestX = prism.Center.X + pointX;
                closestZ = prism.Center.Z + pointZ;
            }
        }

        // The segment overlaps the solid: the shortest of up, each side and down, plus the radius.
        private static void InteriorEscape(in CollisionShape prism, in CapsulePose pose, out double depth, out Vector3 normal, out bool normalIsDefined) {
            double planX = pose.FootPosition.X - prism.Center.X, planZ = pose.FootPosition.Z - prism.Center.Z;
            double up = prism.Center.Y + (double)prism.HalfExtents.Y - pose.SegmentBottom.Y;
            double down = pose.SegmentTop.Y - (prism.Center.Y - (double)prism.HalfExtents.Y);
            double side = -MaxSignedEdge(prism, planX, planZ, out int edge);
            var escapes = new List<double> { up, side, down };
            escapes.Sort();
            depth = escapes[0] + pose.Radius;
            normalIsDefined = escapes[1] - escapes[0] > 1e-3;
            Edge(prism, edge, out double _, out double _, out double edgeX, out double edgeZ);
            double length = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
            normal = escapes[0] == up ? Vector3.UnitY : escapes[0] == down ? -Vector3.UnitY : new Vector3((float)(edgeZ / length), 0f, (float)(-edgeX / length));
        }

        private static double MaxSignedEdge(in CollisionShape prism, double planX, double planZ, out int edge) {
            double largest = double.NegativeInfinity;
            edge = 0;
            for (int index = 0; index < prism.Plan.Count; index++) {
                Edge(prism, index, out double startX, out double startZ, out double edgeX, out double edgeZ);
                double signed = ((planX - startX) * edgeZ - (planZ - startZ) * edgeX) / Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
                if (signed <= largest) continue;

                largest = signed;
                edge = index;
            }
            return largest;
        }

        private static void Edge(in CollisionShape prism, int index, out double startX, out double startZ, out double edgeX, out double edgeZ) {
            prism.Plan.GetVertex(index, out float x, out float z);
            prism.Plan.GetVertex((index + 1) % prism.Plan.Count, out float nextX, out float nextZ);
            startX = x;
            startZ = z;
            edgeX = (double)nextX - x;
            edgeZ = (double)nextZ - z;
        }

        // A convex prism: 3–16 points on a turned ellipse at jittered, ordered angles.
        private static CollisionShape RandomPrism(Random random) {
            int count = 3 + random.Next(14);
            double radiusA = 0.3 + random.NextDouble() * 2.7, radiusB = 0.3 + random.NextDouble() * 2.7, turn = random.NextDouble() * Math.PI * 2.0;
            var offsets = new Vector2[count];
            for (int index = 0; index < count; index++) {
                double angle = (index + (random.NextDouble() - 0.5) * 0.6) * 2.0 * Math.PI / count;
                double localX = radiusA * Math.Cos(angle), localZ = radiusB * Math.Sin(angle);
                offsets[index] = new Vector2((float)(localX * Math.Cos(turn) - localZ * Math.Sin(turn)), (float)(localX * Math.Sin(turn) + localZ * Math.Cos(turn)));
            }
            var center = new Vector3((float)(random.NextDouble() * 10.0 - 5.0), (float)(random.NextDouble() * 4.0 - 2.0), (float)(random.NextDouble() * 10.0 - 5.0));
            return CollisionShape.MakePrism(center, (float)(0.2 + random.NextDouble() * 1.8), offsets);
        }

        private static CapsulePose RandomPose(Random random, in CollisionShape prism) {
            float radius = (float)(0.2 + random.NextDouble() * 0.4);
            float height = radius * 2f + (float)(random.NextDouble() * 2.0);
            double reach = prism.Radius + radius;
            double bottom = prism.Center.Y - prism.HalfExtents.Y - height - 0.3, top = prism.Center.Y + prism.HalfExtents.Y + 0.3;
            var foot = new Vector3(
                prism.Center.X + (float)((random.NextDouble() * 2.0 - 1.0) * reach),
                (float)(bottom + random.NextDouble() * (top - bottom)),
                prism.Center.Z + (float)((random.NextDouble() * 2.0 - 1.0) * reach));
            return new CapsulePose(foot, radius, height);
        }

        // The contact the motor resolves first: the deepest, the earlier on a tie.
        private static CollisionContact Deepest(CollisionContact[] contacts, int count) {
            CollisionContact deepest = contacts[0];
            for (int index = 1; index < count; index++) {
                if (contacts[index].Depth > deepest.Depth) deepest = contacts[index];
            }
            return deepest;
        }

        private static Vector2[] Square(float half) {
            return new[] { new Vector2(-half, -half), new Vector2(half, -half), new Vector2(half, half), new Vector2(-half, half) };
        }

        private static CollisionShape Prism(float bottom, float top) {
            return CollisionShape.MakePrism(new Vector3(0f, (bottom + top) * 0.5f, 0f), (top - bottom) * 0.5f, Square(2f));
        }

        private static CollisionWorld World(CollisionShape[] shapes) {
            return new CollisionWorld(new SceneGeometry("Prisms", 1u, shapes, Array.Empty<MoverDefinition>()), TickSeconds);
        }

        // Steps a grounded pawn along a fixed input, jumping on the first tick when asked.
        private static List<PawnState> Walk(CollisionWorld world, Vector3 start, Vector2 direction, bool jump, int ticks) {
            var profile = new MovementProfile { SlopeLimitDegrees = 45f };
            var state = new PawnState(start, 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var states = new List<PawnState>();
            for (int tick = 0; tick < ticks; tick++) {
                var input = new PawnInput((uint)tick + 1u, direction, WireLocomotion.Walk, jump && tick == 0, false);
                state = PawnMotor.Step(in input, in state, profile, world, (uint)tick + 1u, TickSeconds);
                states.Add(state);
            }
            return states;
        }
    }
}
