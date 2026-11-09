using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>Vertical support on a sloped prism: the plane height and normal over the plan, rim included, nothing outside.</summary>
    public sealed class CollisionSlopedPrismSupportTests {
        private static readonly CollisionShape Wedge = CollisionShape.MakeSlopedPrism(
            Vector3.Zero,
            0f,
            2f,
            0.75f,
            0f,
            new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f) });

        [Fact]
        public void TheTopIsThePlaneWithItsOwnNormal() {
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 3f, 0f, out float height, out Vector3 normal));

            Assert.Equal(2f - 0.375f, height);
            Assert.Equal(-0.6f, normal.X, 6);
            Assert.Equal(0.8f, normal.Y, 6);
            Assert.Equal(0f, normal.Z);
            Assert.Equal(Wedge.SlopedTopHeight(-0.5f, -0.5f), height);
        }

        [Fact]
        public void EdgesAndCornersSupportButOutsideDoesNot() {
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, 0f, 0f, 3f, 0f, out float _, out Vector3 _));
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, 1f, -1f, 3f, 0f, out float corner, out Vector3 _));
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, -1f, 0.5f, 3f, 0f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, 0.01f, 0.01f, 3f, 0f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, -1.01f, 0f, 3f, 0f, out float _, out Vector3 _));
            Assert.Equal(2.75f, corner);
        }

        [Fact]
        public void TheSpanDecidesWhetherTheTopIsReported() {
            Assert.True(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 1.625f, 1.625f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 1.6f, 0f, out float _, out Vector3 _));
            Assert.False(CollisionResolver.TryGetSupport(in Wedge, -0.5f, -0.5f, 5f, 1.7f, out float _, out Vector3 _));
        }

        [Fact]
        public void AFlatTopSupportsLikeAPrism() {
            CollisionShape flat = CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 2f, 0f, 0f, new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f) });
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(0f, 1f, 0f), 1f, new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f) });

            Assert.True(CollisionResolver.TryGetSupport(in flat, -0.5f, 0.25f, 3f, 0f, out float flatHeight, out Vector3 flatNormal));
            Assert.True(CollisionResolver.TryGetSupport(in prism, -0.5f, 0.25f, 3f, 0f, out float prismHeight, out Vector3 prismNormal));
            Assert.Equal(prismHeight, flatHeight);
            Assert.Equal(prismNormal, flatNormal);
        }

        [Fact]
        public void APlacedGroupAndTheHigherPieceWin() {
            CollisionShape low = CollisionShape.MakeSlopedPrism(Vector3.Zero, -1f, 0.5f, 0.25f, 0f, new[] { new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(2f, 2f), new Vector2(-2f, 2f) });
            var world = new CollisionWorld(new SceneGeometry("Sloped", 1u, new[] { low }, Array.Empty<MoverDefinition>()), 1f / 30f);
            world.AddGroup(5ul, new[] { Wedge }, new Vector3(1f, 0f, 0f));

            Assert.True(world.TryGetSupport(0.5f, -0.5f, 3f, -1f, 1u, out SupportHit hit));
            Assert.Equal(Wedge.SlopedTopHeight(-0.5f, -0.5f), hit.Height);
            Assert.Equal(0.8f, hit.Normal.Y, 6);
            Assert.True(world.TryGetSupport(1.9f, 1.5f, 3f, -1f, 1u, out SupportHit lowHit));
            Assert.Equal(0.5f + 0.25f * 1.9f, lowHit.Height, 6);
            Assert.Equal(0, lowHit.ShapeIndex);
        }
    }
}
