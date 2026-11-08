using System;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class FarGroundAnnulusTests {
        [Fact]
        public void AnnulusSpansTheInnerToOuterRadiusFacingUp() {
            FarGroundSettings settings = FarGroundSettings.Default;
            var uv = new Vector2(0.25f, 0.75f);
            FacetMesh mesh = FarGroundAnnulus.Build(settings, uv);

            Assert.Equal(FarGroundAnnulus.Segments * 2, mesh.FaceCount);
            for (int face = 0; face < mesh.FaceCount; face++) Assert.True(mesh.FaceNormal(face).Y > 0.999f, $"face {face} faces {mesh.FaceNormal(face)}");
            foreach (Vector3 corner in mesh.Positions) {
                double radius = Math.Sqrt(corner.X * (double)corner.X + corner.Z * (double)corner.Z);
                bool isInner = Math.Abs(radius - settings.InnerRadius) < 1e-3;
                bool isOuter = Math.Abs(radius - settings.OuterRadius) < 1e-2;
                Assert.True(isInner || isOuter, $"corner at radius {radius}");
                Assert.Equal(0f, corner.Y);
            }
            foreach (Vector2 cornerUv in mesh.Uvs) Assert.Equal(uv, cornerUv);
        }

        [Fact]
        public void CornersStepFiveDegreesCounterClockwiseFromPlusX() {
            Assert.Equal(new Vector3(10f, 0f, 0f), FarGroundAnnulus.OnCircle(10f, 0));
            Assert.Equal(new Vector3(0f, 0f, 10f), FarGroundAnnulus.OnCircle(10f, 18));
            Assert.Equal(new Vector3(-10f, 0f, 0f), FarGroundAnnulus.OnCircle(10f, 36));
            Assert.Equal(new Vector3(0f, 0f, -10f), FarGroundAnnulus.OnCircle(10f, 54));
            Assert.Equal(FarGroundAnnulus.OnCircle(10f, 0), FarGroundAnnulus.OnCircle(10f, FarGroundAnnulus.Segments));
            for (int step = 0; step < FarGroundAnnulus.Segments; step++) {
                Vector3 corner = FarGroundAnnulus.OnCircle(1f, step);
                double angle = step * 5.0 * Math.PI / 180.0;
                Assert.Equal(Math.Cos(angle), corner.X, 6);
                Assert.Equal(Math.Sin(angle), corner.Z, 6);
            }
        }
    }
}
