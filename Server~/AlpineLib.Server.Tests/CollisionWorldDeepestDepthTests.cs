using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Server.Tests.Motor;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The buffer-free deepest-overlap query answers exactly what picking the deepest of the collected contacts does,
    /// including when more shapes overlap than a contact list holds.
    /// </summary>
    public sealed class CollisionWorldDeepestDepthTests {
        private const float TickSeconds = 1f / 30f;

        [Fact]
        public void TheDeepestDepthMatchesTheDeepestCollectedContactInClutter() {
            var world = new CollisionWorld(new SceneGeometry("clutter", 1u, ClutterFields.Dense(3), Array.Empty<MoverDefinition>()), TickSeconds);
            var random = new Random(11);
            int overlapping = 0;
            for (int sample = 0; sample < 4000; sample++) {
                var foot = new Vector3((float)(random.NextDouble() * 14 - 7), (float)(random.NextDouble() * 2 - 0.5), (float)(random.NextDouble() * 14 - 7));
                var pose = new CapsulePose(foot, 0.2f + (float)random.NextDouble() * 0.4f, 1f + (float)random.NextDouble());
                float collected = DeepestCollected(world, in pose);
                if (collected > 0f) overlapping++;
                Assert.Equal(collected, world.DeepestContactDepth(in pose, 1u));
            }

            Assert.True(overlapping > 500, $"only {overlapping} samples overlapped anything");
        }

        [Fact]
        public void TheDeepestDepthStopsWhereTheContactListWouldBeFull() {
            var shapes = new List<CollisionShape>();
            for (int index = 0; index < CollisionWorld.MaxContacts + 6; index++) {
                float angle = index * 0.6f;
                float depthIn = index < CollisionWorld.MaxContacts ? 0.05f : 0.3f;
                shapes.Add(CollisionShape.MakeSphere(new Vector3(MathF.Cos(angle) * (0.6f - depthIn), 0.8f, MathF.Sin(angle) * (0.6f - depthIn)), 0.1f));
            }

            var world = new CollisionWorld(new SceneGeometry("ring", 1u, shapes.ToArray(), Array.Empty<MoverDefinition>()), TickSeconds);
            var pose = new CapsulePose(Vector3.Zero, 0.5f, 1.6f);
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts * 2];
            int count = world.CollectContacts(in pose, 1u, contacts);
            Assert.True(count > CollisionWorld.MaxContacts, "the ring does not overfill a contact list");
            float everyContact = 0f;
            for (int index = 0; index < count; index++) everyContact = MathF.Max(everyContact, contacts[index].Depth);
            Assert.True(everyContact > DeepestCollected(world, in pose), "the deeper spheres are inside the list's reach");
            Assert.Equal(DeepestCollected(world, in pose), world.DeepestContactDepth(in pose, 1u));
        }

        private static float DeepestCollected(CollisionWorld world, in CapsulePose pose) {
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            int count = world.CollectContacts(in pose, 1u, contacts);
            float deepest = 0f;
            for (int index = 0; index < count; index++) {
                if (index == 0 || contacts[index].Depth > deepest) deepest = contacts[index].Depth;
            }

            return deepest;
        }
    }
}
