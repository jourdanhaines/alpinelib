using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>Vertical support on a prism: its flat cap over the plan, edges included, nothing outside.</summary>
    public sealed class CollisionPrismSupportTests {
        private static readonly CollisionShape Wedge = CollisionShape.MakePrism(
            new Vector3(0f, 1f, 0f),
            1f,
            new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f) });

        [Fact]
        public void TheCapIsFlatAndHeldOverThePlan() {
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 3f, 0f, out float height, out Vector3 normal));

            Assert.Equal(2f, height);
            Assert.Equal(new Vector3(0f, 1f, 0f), normal);
        }

        [Fact]
        public void EdgesAndCornersSupportButOutsideDoesNot() {
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, 0f, 0f, 3f, 0f, out float _, out Vector3 _));
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, 1f, -1f, 3f, 0f, out float _, out Vector3 _));
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, -1f, 0.5f, 3f, 0f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, 0.01f, 0.01f, 3f, 0f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, -1.01f, 0f, 3f, 0f, out float _, out Vector3 _));
        }

        [Fact]
        public void TheSpanDecidesWhetherTheCapIsReported() {
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 2f, 2f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 1.9f, 0f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 5f, 2.1f, out float _, out Vector3 _));
        }

        [Fact]
        public void StackedPrismsSupportOnTheUpperCap() {
            Vector2[] square = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            CollisionShape[] stack = { CollisionShape.MakePrism(new Vector3(0f, 0.5f, 0f), 0.5f, square), CollisionShape.MakePrism(new Vector3(0f, 1.5f, 0f), 0.5f, square) };
            var world = new CollisionWorld(new SceneGeometry("Stack", 1u, stack, Array.Empty<MoverDefinition>()), 1f / 30f);

            Assert.True(world.TryGetSupport(0.3f, -0.4f, 2.3f, 1.7f, 1u, out SupportHit top));
            Assert.Equal(2f, top.Height);
            Assert.Equal(1, top.ShapeIndex);
            Assert.True(world.TryGetSupport(1f, 1f, 3f, -1f, 1u, out SupportHit corner));
            Assert.Equal(2f, corner.Height);
        }

        [Fact]
        public void APlacedGroupSupportsAtItsOffset() {
            CollisionWorld world = new CollisionWorld(new SceneGeometry("Prisms", 1u, Array.Empty<CollisionShape>(), Array.Empty<MoverDefinition>()), 1f / 30f);
            world.AddGroup(5ul, new[] { Wedge }, new Vector3(40f, 0.5f, -12f));

            Assert.True(world.TryGetSupport(39.5f, -12.5f, 3f, 0f, 1u, out SupportHit hit));
            Assert.Equal(2.5f, hit.Height);
            Assert.False(world.TryGetSupport(-0.5f, -0.5f, 3f, 0f, 1u, out SupportHit _));
        }
    }
}
