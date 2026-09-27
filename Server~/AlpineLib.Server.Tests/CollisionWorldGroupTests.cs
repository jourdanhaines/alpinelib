using System;
using System.Numerics;
using System.Text;
using AlpineLib.Netcode.Collision;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Keyed static groups in a collision world: placed at their offset, removable, shifted together by a
    /// rebase, and flattened in key order so the same groups answer identically however they arrived.
    /// </summary>
    public sealed class CollisionWorldGroupTests {
        private const float TickSeconds = 1f / 30f;

        [Fact]
        public void TheScenesStaticsAreGroupZeroExactlyAsExported() {
            CollisionShape[] exported = { Box(new Vector3(1.1f, 0.3f, -2.7f), 0.5f), Box(new Vector3(4f, 1f, 4f), 1f) };
            CollisionWorld world = new CollisionWorld(Geometry(exported), TickSeconds);

            Assert.Equal(1, world.GroupCount);
            Assert.True(world.HasGroup(CollisionWorld.SceneGroupKey));
            Assert.Equal(2, world.StaticShapeCount);
            Assert.Equal(exported[1].Center, world.GetStaticShape(1).Center);
            Assert.True(world.TryResolveShape(1, out ulong key, out int localIndex));
            Assert.Equal(CollisionWorld.SceneGroupKey, key);
            Assert.Equal(1, localIndex);
        }

        [Fact]
        public void TheFlatFallbackIsUnchanged() {
            CollisionWorld world = CollisionWorld.Flat(2f);

            Assert.True(world.IsFallback);
            Assert.Equal(1, world.StaticShapeCount);
            Assert.Equal(CollisionShapeType.Plane, world.GetStaticShape(0).Type);
            Assert.True(world.TryGetSupport(500f, -500f, 10f, -10f, 1u, out SupportHit hit));
            Assert.Equal(2f, hit.Height);
        }

        [Fact]
        public void AnAddedGroupStandsAtItsOffset() {
            CollisionWorld world = EmptyWorld();
            Assert.False(world.TryGetSupport(100f, 50f, 10f, -10f, 1u, out SupportHit _));

            world.AddGroup(7ul, new[] { Box(new Vector3(0f, -0.5f, 0f), 2f) }, new Vector3(100f, 0f, 50f));

            Assert.True(world.TryGetSupport(100f, 50f, 10f, -10f, 1u, out SupportHit hit));
            Assert.Equal(1.5f, hit.Height);
            Assert.True(world.TryResolveShape(hit.ShapeIndex, out ulong key, out int _));
            Assert.Equal(7ul, key);
        }

        [Fact]
        public void ARemovedGroupStopsColliding() {
            CollisionWorld world = EmptyWorld();
            world.AddGroup(3ul, new[] { Box(Vector3.Zero, 1f) }, new Vector3(10f, 0f, 10f));

            Assert.True(world.RemoveGroup(3ul));

            Assert.False(world.TryGetSupport(10f, 10f, 10f, -10f, 1u, out SupportHit _));
            Assert.False(world.RemoveGroup(3ul));
            Assert.Equal(0, world.StaticShapeCount);
        }

        [Fact]
        public void TheCallersArrayIsCopied() {
            CollisionWorld world = EmptyWorld();
            CollisionShape[] shapes = { Box(Vector3.Zero, 1f) };
            world.AddGroup(1ul, shapes, Vector3.Zero);

            shapes[0] = Box(new Vector3(0f, 50f, 0f), 1f);

            Assert.Equal(Vector3.Zero, world.GetStaticShape(0).Center);
        }

        [Fact]
        public void AddingTheSameKeyTwiceIsRefused() {
            CollisionWorld world = EmptyWorld();
            world.AddGroup(4ul, Array.Empty<CollisionShape>(), Vector3.Zero);

            Assert.Throws<ArgumentException>(() => world.AddGroup(4ul, Array.Empty<CollisionShape>(), Vector3.Zero));
            Assert.Throws<ArgumentNullException>(() => world.AddGroup(5ul, null, Vector3.Zero));
        }

        [Fact]
        public void GroupsFlattenInKeyOrderWhateverOrderTheyArrived() {
            CollisionWorld forward = EmptyWorld();
            CollisionWorld shuffled = EmptyWorld();
            ulong[] keys = { 2ul, 5ul, 9ul, 40ul };
            ulong[] arrival = { 40ul, 2ul, 9ul, 5ul };

            foreach (ulong key in keys) {
                forward.AddGroup(key, OverlappingPile(key), GroupOffset(key));
            }

            foreach (ulong key in arrival) {
                shuffled.AddGroup(key, OverlappingPile(key), GroupOffset(key));
            }

            Assert.Equal(forward.StaticShapeCount, shuffled.StaticShapeCount);

            for (int shapeIndex = 0; shapeIndex < forward.StaticShapeCount; shapeIndex++) {
                Assert.Equal(forward.GetStaticShape(shapeIndex).Center, shuffled.GetStaticShape(shapeIndex).Center);
            }

            Assert.Equal(Contacts(forward), Contacts(shuffled));
        }

        [Fact]
        public void AnOverSubscribedQueryDropsTheHighestKeysOnBothEnds() {
            CollisionWorld forward = EmptyWorld();
            CollisionWorld backward = EmptyWorld();
            ulong[] keys = { 10ul, 20ul, 30ul };

            for (int index = 0; index < keys.Length; index++) {
                forward.AddGroup(keys[index], Pile(30), Vector3.Zero);
                backward.AddGroup(keys[keys.Length - 1 - index], Pile(30), Vector3.Zero);
            }

            int[] forwardCandidates = Candidates(forward, out int forwardCount);
            int[] backwardCandidates = Candidates(backward, out int backwardCount);

            Assert.Equal(CollisionWorld.MaxCandidates, forwardCount);
            Assert.Equal(forwardCandidates, backwardCandidates);
            Assert.Equal(forwardCount, backwardCount);
            Assert.True(forward.TryResolveShape(forwardCandidates[forwardCount - 1], out ulong lastKey, out int lastLocal));
            Assert.Equal(30ul, lastKey);
            Assert.Equal(CollisionWorld.MaxCandidates - 61, lastLocal);
        }

        [Fact]
        public void TranslateAllShiftsEveryGroupAndMover() {
            MoverDefinition mover = new MoverDefinition(1, 1, Box(Vector3.Zero, 1f),
                new MoverPath(new[] { new Vector3(0f, 50f, 0f), new Vector3(10f, 50f, 0f) }, 1f, MoverLoopMode.PingPong, 0u));
            CollisionWorld world = new CollisionWorld(
                new SceneGeometry("Scene", 1u, new[] { Box(new Vector3(0f, -1f, 0f), 1f) }, new[] { mover }), TickSeconds);
            world.AddGroup(9ul, new[] { Box(new Vector3(0f, -1f, 0f), 1f) }, new Vector3(128f, 0f, 0f));
            Vector3 moverBefore = world.EvaluateMoverPosition(0, 15u);

            world.TranslateAll(new Vector3(-128f, 0f, 0f));

            Assert.True(world.TryGetSupport(0f, 0f, 10f, -10f, 1u, out SupportHit shifted));
            Assert.True(world.TryResolveShape(shifted.ShapeIndex, out ulong key, out int _));
            Assert.Equal(9ul, key);
            Assert.True(world.TryGetSupport(-128f, 0f, 10f, -10f, 1u, out SupportHit scene));
            Assert.Equal(0f, scene.Height);
            Assert.Equal(new Vector3(moverBefore.X - 128f, moverBefore.Y, moverBefore.Z), world.EvaluateMoverPosition(0, 15u));
            Assert.Equal(new Vector3(-128f, 0f, 0f), world.MoverOffset);
        }

        [Fact]
        public void AGroupAddedAfterARebaseIsPlacedInTheNewFrame() {
            CollisionWorld world = EmptyWorld();
            world.TranslateAll(new Vector3(-1000f, 0f, 0f));

            world.AddGroup(1ul, new[] { Box(new Vector3(0f, -1f, 0f), 1f) }, new Vector3(5f, 0f, 5f));

            Assert.True(world.TryGetSupport(5f, 5f, 10f, -10f, 1u, out SupportHit _));
        }

        [Fact]
        public void TheBroadPhaseIsRebuiltOnlyAfterAChange() {
            CollisionWorld world = EmptyWorld();
            world.AddGroup(1ul, Pile(3), Vector3.Zero);
            CollisionGrid first = world.Grid;
            int revision = world.Revision;

            world.TryGetSupport(0f, 0f, 10f, -10f, 1u, out SupportHit _);

            Assert.Same(first, world.Grid);
            Assert.Equal(revision, world.Revision);

            world.AddGroup(2ul, Pile(3), Vector3.Zero);

            Assert.NotSame(first, world.Grid);
            Assert.Equal(revision + 1, world.Revision);
            Assert.Equal(6, world.Grid.ShapeCount);
        }

        [Fact]
        public void AGroupBeyondTheBucketedRangeStillCollides() {
            CollisionWorld world = EmptyWorld();
            float far = CollisionGrid.MaxBucketedCoordinate * 2f;
            world.AddGroup(1ul, new[] { Box(new Vector3(0f, -1f, 0f), 4f) }, new Vector3(far, 0f, 0f));
            world.AddGroup(2ul, new[] { Box(new Vector3(0f, -1f, 0f), 4f) }, Vector3.Zero);

            Assert.True(world.TryGetSupport(far, 0f, 10f, -10f, 1u, out SupportHit farHit));
            Assert.True(world.TryResolveShape(farHit.ShapeIndex, out ulong farKey, out int _));
            Assert.Equal(1ul, farKey);
            Assert.True(world.TryGetSupport(0f, 0f, 10f, -10f, 1u, out SupportHit _));
        }

        [Fact]
        public void EmptyGroupsResolveShapesToTheGroupThatHoldsThem() {
            CollisionWorld world = EmptyWorld();
            world.AddGroup(1ul, Array.Empty<CollisionShape>(), Vector3.Zero);
            world.AddGroup(2ul, Pile(2), Vector3.Zero);
            world.AddGroup(3ul, Array.Empty<CollisionShape>(), Vector3.Zero);

            Assert.True(world.TryResolveShape(1, out ulong key, out int localIndex));
            Assert.Equal(2ul, key);
            Assert.Equal(1, localIndex);
            Assert.False(world.TryResolveShape(2, out ulong _, out int _));
        }

        private static CollisionWorld EmptyWorld() {
            CollisionWorld world = new CollisionWorld(Geometry(Array.Empty<CollisionShape>()), TickSeconds);
            world.RemoveGroup(CollisionWorld.SceneGroupKey);
            return world;
        }

        private static SceneGeometry Geometry(CollisionShape[] shapes) {
            return new SceneGeometry("Scene", 1u, shapes, Array.Empty<MoverDefinition>());
        }

        private static CollisionShape Box(Vector3 center, float halfExtent) {
            return CollisionShape.MakeBox(center, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(halfExtent));
        }

        private static CollisionShape[] Pile(int count) {
            CollisionShape[] shapes = new CollisionShape[count];

            for (int index = 0; index < count; index++) {
                shapes[index] = Box(new Vector3(0.01f * index, 0f, 0f), 1f);
            }

            return shapes;
        }

        /// <summary>Boxes around the probe capsule so several groups contribute contacts at once.</summary>
        private static CollisionShape[] OverlappingPile(ulong key) {
            float side = key % 2ul == 0ul ? 0.9f : -0.9f;
            return new[] { Box(new Vector3(side, 1f, 0f), 0.5f), Box(new Vector3(0f, 1f, side), 0.5f) };
        }

        private static Vector3 GroupOffset(ulong key) {
            return new Vector3(0.01f * key, 0f, -0.01f * key);
        }

        private static int[] Candidates(CollisionWorld world, out int count) {
            int[] candidates = new int[CollisionWorld.MaxCandidates];
            count = world.Grid.Collect(-0.5f, 0.5f, -0.5f, 0.5f, candidates);
            return candidates;
        }

        private static string Contacts(CollisionWorld world) {
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            CapsulePose pose = new CapsulePose(new Vector3(0f, 0f, 0f), 0.5f, 1.8f);
            int written = world.CollectContacts(in pose, 1u, contacts);
            StringBuilder summary = new StringBuilder();

            for (int index = 0; index < written; index++) {
                CollisionContact contact = contacts[index];
                summary.Append(contact.ShapeIndex).Append(':').Append(contact.Normal).Append(':').Append(contact.Depth).Append(';');
            }

            Assert.True(written > 2, "The probe should touch shapes from several groups.");
            return summary.ToString();
        }
    }
}
