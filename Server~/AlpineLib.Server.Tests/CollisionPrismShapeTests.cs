using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>The prism primitive: construction rules, its wire form inside a geometry file, and its bounds.</summary>
    public sealed class CollisionPrismShapeTests {
        private const int PlainShapeByteCount = 69;
        private const int VersionOffset = 4;

        [Fact]
        public void APrismKeepsItsPlanCentredOnTheBoundingBox() {
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(10f, 2f, -4f), 1.5f, Pentagon(new Vector2(1f, 0.5f)));

            Assert.Equal(CollisionShapeType.Prism, prism.Type);
            Assert.Equal(5, prism.Plan.Count);
            Assert.Equal(new Vector3(11f, 2f, -3.5f), prism.Center);
            Assert.Equal(new Vector3(2f, 1.5f, 1.5f), prism.HalfExtents);
            Assert.Equal(Vector3.UnitX, prism.AxisRight);
            Assert.Equal(Vector3.UnitY, prism.AxisUp);
            Assert.Equal(Vector3.UnitZ, prism.AxisForward);
            prism.Plan.GetVertex(0, out float x, out float z);
            Assert.Equal(-2f, x);
            Assert.Equal(-1.5f, z);
            Assert.Equal(MathF.Sqrt(2f * 2f + 1.5f * 1.5f), prism.Radius);
        }

        [Fact]
        public void BoundsAndTranslationGoThroughTheBoxPath() {
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(0f, 1f, 0f), 1f, Pentagon(Vector2.Zero));

            prism.GetBoundsXZ(out float minX, out float maxX, out float minZ, out float maxZ);
            Assert.Equal(-2f, minX);
            Assert.Equal(2f, maxX);
            Assert.Equal(-1.5f, minZ);
            Assert.Equal(1.5f, maxZ);

            CollisionShape moved = prism.Translated(new Vector3(5f, 1f, -3f));
            moved.GetBoundsXZ(out minX, out maxX, out minZ, out maxZ);
            Assert.Equal(3f, minX);
            Assert.Equal(7f, maxX);
            Assert.Equal(-4.5f, minZ);
            Assert.Equal(-1.5f, maxZ);
            Assert.Equal(new Vector3(5f, 2f, -3f), moved.Center);
            Assert.Equal(prism.Plan, moved.Plan);
        }

        [Fact]
        public void DegeneratePlansAreRefused() {
            Vector2[] square = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            Vector2[] clockwise = { square[3], square[2], square[1], square[0] };
            Vector2[] collinear = { new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            Vector2[] repeated = { square[0], square[1], square[1], square[2], square[3] };
            Vector2[] two = { square[0], square[1] };
            Vector2[] nine = new Vector2[9];
            for (int index = 0; index < nine.Length; index++) nine[index] = new Vector2(index, index * index);
            Vector2[] notANumber = { square[0], new Vector2(float.NaN, -1f), square[2], square[3] };
            Vector2[] windsTwice = Star();

            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 1f, clockwise));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 1f, collinear));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 1f, repeated));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 1f, two));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 1f, nine));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 1f, notANumber));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 1f, windsTwice));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(Vector3.Zero, 0f, square));
            Assert.Throws<ArgumentException>(() => CollisionShape.MakePrism(new Vector3(float.PositiveInfinity, 0f, 0f), 1f, square));
            Assert.Equal(4, CollisionShape.MakePrism(Vector3.Zero, 1f, square).Plan.Count);
        }

        [Fact]
        public void OtherShapesKeepTheirExactBytes() {
            CollisionShape box = CollisionShape.MakeBox(new Vector3(1f, 2f, 3f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.One);

            Assert.Equal(PlainShapeByteCount, Serialize(box).Length);
            Assert.Equal(PlainShapeByteCount + 1 + 5 * 8, Serialize(CollisionShape.MakePrism(Vector3.Zero, 1f, Pentagon(Vector2.Zero))).Length);
        }

        [Fact]
        public void AGeometryFileRoundTripsPrismsBitForBit() {
            CollisionShape prism = CollisionShape.MakePrism(new Vector3(0.1f, 0.2f, 0.3f), 0.7f, Pentagon(new Vector2(0.01f, -0.02f)));
            CollisionShape box = CollisionShape.MakeBox(Vector3.One, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.One);
            var mover = new MoverDefinition(3, 4, in prism, new MoverPath(new[] { Vector3.Zero, Vector3.One }, 1f, MoverLoopMode.PingPong, 0u));
            var geometry = new SceneGeometry("Prisms", 0u, new[] { box, prism }, new[] { mover });

            SceneGeometry decoded = SceneGeometryCodec.Decode(SceneGeometryCodec.Encode(geometry));

            Assert.Equal(SceneGeometryCodec.ComputeContentHash(geometry), decoded.ContentHash);
            Assert.Equal(prism, decoded.StaticShapes[1]);
            Assert.Equal(box, decoded.StaticShapes[0]);
            Assert.Equal(prism, decoded.Movers[0].LocalShape);
        }

        [Fact]
        public void AVersionOneFileStillReadsButCannotHoldAPrism() {
            CollisionShape box = CollisionShape.MakeBox(Vector3.One, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.One);
            byte[] plain = SceneGeometryCodec.Encode(new SceneGeometry("Old", 0u, new[] { box }, Array.Empty<MoverDefinition>()));
            byte[] withPrism = SceneGeometryCodec.Encode(new SceneGeometry("Old", 0u, new[] { CollisionShape.MakePrism(Vector3.Zero, 1f, Pentagon(Vector2.Zero)) }, Array.Empty<MoverDefinition>()));
            Assert.Equal(2, BitConverter.ToUInt16(plain, VersionOffset));

            plain[VersionOffset] = 1;
            withPrism[VersionOffset] = 1;

            Assert.Equal(box, SceneGeometryCodec.Decode(plain).StaticShapes[0]);
            Assert.Throws<NetProtocolException>(() => SceneGeometryCodec.Decode(withPrism));
            plain[VersionOffset] = 3;
            Assert.Throws<NetProtocolException>(() => SceneGeometryCodec.Decode(plain));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(9)]
        [InlineData(255)]
        public void ABadVertexCountIsRefusedBeforeAnyFloatIsRead(byte count) {
            byte[] bytes = Serialize(CollisionShape.MakePrism(Vector3.Zero, 1f, Pentagon(Vector2.Zero)));
            bytes[PlainShapeByteCount] = count;

            Assert.Throws<NetProtocolException>(() => Deserialize(bytes));
            Assert.Throws<NetProtocolException>(() => Deserialize(bytes.AsSpan(0, PlainShapeByteCount + 1).ToArray()));
        }

        [Fact]
        public void AZeroLengthEdgeIsRefusedOnRead() {
            byte[] bytes = Serialize(CollisionShape.MakePrism(Vector3.Zero, 1f, Pentagon(Vector2.Zero)));
            Array.Copy(bytes, PlainShapeByteCount + 1, bytes, PlainShapeByteCount + 1 + 8, 8);

            Assert.Throws<NetProtocolException>(() => Deserialize(bytes));
        }

        /// <summary>A CCW pentagon whose bounding box is [−2, 2] × [−1.5, 1.5] about <paramref name="shift"/>.</summary>
        internal static Vector2[] Pentagon(Vector2 shift) {
            return new[] {
                new Vector2(-2f, -1.5f) + shift,
                new Vector2(2f, -1.5f) + shift,
                new Vector2(2f, 0.5f) + shift,
                new Vector2(0f, 1.5f) + shift,
                new Vector2(-2f, 0.5f) + shift,
            };
        }

        // A pentagram: every turn is to the left, yet it winds twice.
        private static Vector2[] Star() {
            var points = new Vector2[5];
            for (int index = 0; index < 5; index++) {
                double angle = 2.0 * Math.PI * (index * 2 % 5) / 5.0;
                points[index] = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
            return points;
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
