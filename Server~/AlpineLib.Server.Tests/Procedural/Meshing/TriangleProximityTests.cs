using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class TriangleProximityTests {
        private static readonly List<Vector3> Block = EnvelopeShapes.Cuboid(new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 1f), 0);

        [Fact]
        public void APointInsideTheSolidIsInsideAndOneOutsideIsNot() {
            var look = new TriangleProximity(Block, 0.5);

            Assert.True(look.IsInside(0.3, 1.0, -0.2));
            Assert.True(look.IsInside(-0.9, 0.1, 0.9));
            Assert.False(look.IsInside(1.3, 1.0, 0.0));
            Assert.False(look.IsInside(0.0, 2.5, 0.0));
            Assert.False(look.IsInside(-3.0, 1.0, 0.0));
        }

        [Fact]
        public void OverlappingShellsStayInside() {
            var looks = new List<Vector3>(Block);
            looks.AddRange(EnvelopeShapes.Cuboid(new Vector3(0.5f, 1f, 0f), new Vector3(1f, 1f, 1f), 0));
            var look = new TriangleProximity(looks, 0.5);

            Assert.True(look.IsInside(0.25, 1.0, 0.0));
            Assert.True(look.IsInside(1.3, 1.0, 0.0));
            Assert.False(look.IsInside(1.7, 1.0, 0.0));
        }

        [Fact]
        public void AColumnGrazingAFaceOutlineFindsIt() {
            var look = new TriangleProximity(Block, 0.5);

            Assert.Equal(2.0, look.HeightBelow(1.01, 0.0, 3.0, 0.0), 5);
            Assert.Equal(0.0, look.HeightBelow(1.05, 0.0, 3.0, 0.0), 5);
        }
    }
}
