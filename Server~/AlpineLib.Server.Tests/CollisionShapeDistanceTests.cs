using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using Xunit;

namespace AlpineLib.Server.Tests {
    public sealed class CollisionShapeDistanceTests {
        [Fact]
        public void ATurnedBoxMeasuresAlongItsOwnAxes() {
            float half = (float)Math.Sqrt(0.5);
            var right = new Vector3(half, 0f, -half);
            var forward = new Vector3(half, 0f, half);
            CollisionShape box = CollisionShape.MakeBox(new Vector3(0f, 1f, 0f), right, Vector3.UnitY, forward, new Vector3(1f, 1f, 1f));

            Assert.Equal(0.0, CollisionShapeDistance.Outside(box, new Vector3(0.9f, 1f, 0f)));
            Assert.Equal(0.0, CollisionShapeDistance.Outside(box, new Vector3((float)Math.Sqrt(2.0), 1f, 0f)), 4);
            Assert.Equal(1.0, CollisionShapeDistance.Outside(box, new Vector3(0f, 1f, 0f) + right * 2f), 4);
            Assert.Equal(0.5, CollisionShapeDistance.Outside(box, new Vector3(0f, 2.5f, 0f)), 5);
        }

        [Fact]
        public void CapsuleSphereAndPlaneMeasureToTheirSolids() {
            CollisionShape capsule = CollisionShape.MakeCapsule(Vector3.Zero, Vector3.UnitY, 1f, 0.5f);
            CollisionShape sphere = CollisionShape.MakeSphere(new Vector3(5f, 0f, 0f), 1f);
            CollisionShape plane = CollisionShape.MakePlane(0f);

            Assert.Equal(0.0, CollisionShapeDistance.Outside(capsule, new Vector3(0.4f, 1.2f, 0f)));
            Assert.Equal(0.5, CollisionShapeDistance.Outside(capsule, new Vector3(0f, 2f, 0f)), 5);
            Assert.Equal(1.5, CollisionShapeDistance.Outside(capsule, new Vector3(2f, 0f, 0f)), 5);
            Assert.Equal(1.0, CollisionShapeDistance.Outside(sphere, new Vector3(5f, 2f, 0f)), 5);
            Assert.Equal(0.0, CollisionShapeDistance.Outside(plane, new Vector3(3f, -1f, 0f)));
            Assert.Equal(2.0, CollisionShapeDistance.Outside(plane, new Vector3(3f, 2f, 0f)), 5);
        }

        [Fact]
        public void APrismMeasuresInPlanAndInHeight() {
            Vector2[] square = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(0f, 1f, 0f), 1f, square);

            Assert.Equal(0.0, CollisionShapeDistance.Outside(prism, new Vector3(0.9f, 1.9f, -0.9f)));
            Assert.Equal(0.0, CollisionShapeDistance.Outside(prism, new Vector3(1f, 2f, 1f)));
            Assert.Equal(0.5, CollisionShapeDistance.Outside(prism, new Vector3(1.5f, 1f, 0f)), 6);
            Assert.Equal(0.5, CollisionShapeDistance.Outside(prism, new Vector3(0f, 2.5f, 0f)), 6);
            Assert.Equal(Math.Sqrt(0.5 * 0.5 + 0.5 * 0.5 + 0.5 * 0.5), CollisionShapeDistance.Outside(prism, new Vector3(1.5f, -0.5f, -1.5f)), 6);
        }

        [Fact]
        public void APrismsSlantedEdgeMeasuresAlongItsNormal() {
            Vector2[] triangle = { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(0f, 2f) };
            CollisionShape prism = CollisionShape.MakePrism(Vector3.Zero, 1f, triangle);

            Assert.Equal(Math.Sqrt(2.0), CollisionShapeDistance.Outside(prism, new Vector3(2f, 0f, 2f)), 5);
            Assert.Equal(0.0, CollisionShapeDistance.Outside(prism, new Vector3(0.5f, 0f, 0.5f)));
        }

        [Fact]
        public void TheNearestOfARangeWins() {
            var shapes = new[] {
                CollisionShape.MakeSphere(new Vector3(10f, 0f, 0f), 1f),
                CollisionShape.MakeSphere(new Vector3(3f, 0f, 0f), 1f),
                CollisionShape.MakeSphere(new Vector3(0f, 0f, 0f), 1f),
            };

            Assert.Equal(2.0, CollisionShapeDistance.Outside(shapes, 0, 2, Vector3.Zero), 5);
            Assert.Equal(0.0, CollisionShapeDistance.Outside(shapes, 0, 3, Vector3.Zero));
            Assert.Equal(double.PositiveInfinity, CollisionShapeDistance.Outside(shapes, 0, 0, Vector3.Zero));
        }
    }
}
