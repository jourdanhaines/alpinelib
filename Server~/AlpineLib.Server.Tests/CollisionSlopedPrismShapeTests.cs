using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>The sloped prism primitive: construction rules, bounds, translation and its wire and file forms.</summary>
    public sealed class CollisionSlopedPrismShapeTests {
        private const int PlainShapeByteCount = 69;
        private const int VersionOffset = 4;

        [Fact]
        public void TheTopIsReExpressedAtTheRecentredCentre() {
            Vector2[] plan = CollisionPrismShapeTests.Pentagon(new Vector2(1f, 0.5f));

            CollisionShape shape = CollisionShape.MakeSlopedPrism(new Vector3(10f, 99f, -4f), 1f, 3f, 0.5f, -0.25f, plan);

            Assert.Equal(CollisionShapeType.SlopedPrism, shape.Type);
            Assert.True(shape.HasPlan);
            Assert.Equal(new Vector3(11f, shape.Center.Y, -3.5f), shape.Center);
            Assert.Equal(1f, shape.BottomY);
            Assert.Equal(3f + 0.5f * 1f - 0.25f * 0.5f, shape.TopY0);
            Assert.Equal(0.5f, shape.TopSlopeX);
            Assert.Equal(-0.25f, shape.TopSlopeZ);
            Assert.Equal(3f, shape.SlopedTopHeight(10f, -4f));
            Assert.Equal(3f + 0.5f * 2f, shape.SlopedTopHeight(12f, -4f));

            // Highest vertex: (2, −1.5) offset → 3.375 + 1 + 0.375.
            float highest = 3.375f + 1f + 0.375f;
            Assert.Equal((1f + highest) * 0.5f, shape.Center.Y);
            Assert.Equal(new Vector3(2f, (highest - 1f) * 0.5f, 1.5f), shape.HalfExtents);
            Assert.Equal((float)2.5001, shape.Radius);
        }

        [Fact]
        public void BoundsAndTranslationMoveThePlane() {
            CollisionShape shape = CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 2f, 0.5f, 0f, CollisionPrismShapeTests.Pentagon(Vector2.Zero));

            shape.GetBoundsXZ(out float minX, out float maxX, out float minZ, out float maxZ);
            Assert.Equal(-2f, minX);
            Assert.Equal(2f, maxX);
            Assert.Equal(-1.5f, minZ);
            Assert.Equal(1.5f, maxZ);

            CollisionShape moved = shape.Translated(new Vector3(5f, 1f, -3f));
            Assert.Equal(1f, moved.BottomY);
            Assert.Equal(3f, moved.TopY0);
            Assert.Equal(shape.SlopedTopHeight(1f, 1f) + 1f, moved.SlopedTopHeight(6f, -2f));
            Assert.Equal(shape.Center.Y + 1f, moved.Center.Y);
            Assert.Equal(shape.Plan, moved.Plan);
        }

        [Fact]
        public void DegenerateShapesAreRefused() {
            Vector2[] square = Square(1f);
            Vector2[] clockwise = { square[3], square[2], square[1], square[0] };
            Vector2[] two = { square[0], square[1] };
            var seventeen = new Vector2[17];
            for (int index = 0; index < seventeen.Length; index++) seventeen[index] = new Vector2(index, index * index);

            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 1f, 0.5f, 0f, 0f, square));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 1f, 0.995f, 0f, square));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 5f, 1.01f, 0f, square));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 5f, 0.8f, 0.8f, square));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 5f, 0f, 0f, clockwise));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 5f, 0f, 0f, two));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 5f, 0f, 0f, seventeen));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, float.NaN, 5f, 0f, 0f, square));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 5f, float.NaN, 0f, square));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakeSlopedPrism(new Vector3(float.PositiveInfinity, 0f, 0f), 0f, 5f, 0f, 0f, square));

            Assert.Equal(0.7f, CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 2f, 0.6f, 0.7f, square).TopSlopeZ);
            Assert.Equal(CollisionShapeType.SlopedPrism, CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 1.02f, 1f, 0f, square).Type);
        }

        [Fact]
        public void TheWireFormAppendsThePlaneAfterThePlan() {
            CollisionShape shape = CollisionShape.MakeSlopedPrism(new Vector3(1f, 0f, 3f), -2f, 1f, 0.25f, -0.5f, CollisionPrismShapeTests.Pentagon(Vector2.Zero));
            byte[] bytes = Serialize(shape);

            Assert.Equal(PlainShapeByteCount + 1 + 5 * 8 + 4 * 4, bytes.Length);
            Assert.Equal(shape, Deserialize(bytes));
            Assert.Equal(PlainShapeByteCount + 1 + 5 * 8, Serialize(CollisionShape.MakePrism(Vector3.Zero, 1f, CollisionPrismShapeTests.Pentagon(Vector2.Zero))).Length);
        }

        [Theory]
        [InlineData(0, float.NaN)]
        [InlineData(1, float.PositiveInfinity)]
        [InlineData(2, 1.5f)]
        [InlineData(3, -1.2f)]
        [InlineData(0, 10f)]
        [InlineData(1, -10f)]
        public void ABadPlaneIsRefusedOnRead(int field, float value) {
            CollisionShape shape = CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 2f, 0.25f, 0f, CollisionPrismShapeTests.Pentagon(Vector2.Zero));
            byte[] bytes = Serialize(shape);
            int offset = PlainShapeByteCount + 1 + 5 * 8 + field * 4;
            BitConverter.GetBytes(value).CopyTo(bytes, offset);

            Assert.Throws<NetProtocolException>(() => Deserialize(bytes));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(17)]
        public void ABadVertexCountIsRefused(byte count) {
            byte[] bytes = Serialize(CollisionShape.MakeSlopedPrism(Vector3.Zero, 0f, 2f, 0f, 0f, CollisionPrismShapeTests.Pentagon(Vector2.Zero)));
            bytes[PlainShapeByteCount] = count;

            Assert.Throws<NetProtocolException>(() => Deserialize(bytes));
        }

        [Fact]
        public void AGeometryFileRoundTripsSlopedPrismsAndOlderVersionsCannotHoldThem() {
            CollisionShape sloped = CollisionShape.MakeSlopedPrism(new Vector3(0.1f, 0f, 0.3f), 0.2f, 1.7f, 0.31f, -0.17f, CollisionPrismShapeTests.Pentagon(new Vector2(0.01f, -0.02f)));
            CollisionShape prism = CollisionShape.MakePrism(Vector3.One, 0.5f, CollisionPrismShapeTests.Pentagon(Vector2.Zero));
            CollisionShape box = CollisionShape.MakeBox(Vector3.One, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.One);
            var mover = new MoverDefinition(3, 4, in sloped, new MoverPath(new[] { Vector3.Zero, Vector3.One }, 1f, MoverLoopMode.PingPong, 0u));
            byte[] current = SceneGeometryCodec.Encode(new SceneGeometry("Sloped", 0u, new[] { box, prism, sloped }, new[] { mover }));
            byte[] withPrism = SceneGeometryCodec.Encode(new SceneGeometry("Prism", 0u, new[] { box, prism }, Array.Empty<MoverDefinition>()));
            byte[] plain = SceneGeometryCodec.Encode(new SceneGeometry("Plain", 0u, new[] { box }, Array.Empty<MoverDefinition>()));

            SceneGeometry decoded = SceneGeometryCodec.Decode(current);
            Assert.Equal(3, BitConverter.ToUInt16(current, VersionOffset));
            Assert.Equal(sloped, decoded.StaticShapes[2]);
            Assert.Equal(prism, decoded.StaticShapes[1]);
            Assert.Equal(sloped, decoded.Movers[0].LocalShape);

            withPrism[VersionOffset] = 2;
            Assert.Equal(prism, SceneGeometryCodec.Decode(withPrism).StaticShapes[1]);
            plain[VersionOffset] = 1;
            Assert.Equal(box, SceneGeometryCodec.Decode(plain).StaticShapes[0]);
            current[VersionOffset] = 2;
            Assert.Throws<NetProtocolException>(() => SceneGeometryCodec.Decode(current));
        }

        private static Vector2[] Square(float half) {
            return new[] { new Vector2(-half, -half), new Vector2(half, -half), new Vector2(half, half), new Vector2(-half, half) };
        }

        private static byte[] Serialize(CollisionShape shape) {
            var buffer = new byte[256];
            var writer = new NetWriter(buffer);
            writer.WriteMessage(in shape);
            return writer.ToArray();
        }

        private static CollisionShape Deserialize(byte[] bytes) {
            var reader = new NetReader(bytes);
            return reader.ReadMessage<CollisionShape>();
        }
    }
}
