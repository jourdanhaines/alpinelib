using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class BoxEnvelopeFitterTests {
        private static readonly double[] MetreBands = { 0.5, 1.0, 1.5, 2.0, 2.5, 3.0 };

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(8)]
        public void BoxesHoldEveryCornerAndCentroidOfABlob(int maxBoxes) {
            BlobSpec spec = BlobSpec.Default;
            spec.Subdivisions = 2;
            spec.Radii = new Vector3(1.5f, 1.2f, 0.9f);
            spec.NoiseAmplitude = 0.3f;
            spec.FacetJitter = 0.1f;
            FacetMesh mesh = BlobMesh.Build(spec, 7UL);
            var settings = new BoxEnvelopeSettings { BandEdges = new[] { -0.6, -0.2, 0.2, 0.6 }, MaxBoxes = maxBoxes };

            List<EnvelopeBox> boxes = BoxEnvelopeFitter.Fit(mesh.Positions, settings);

            Assert.InRange(boxes.Count, 1, maxBoxes);
            foreach (Vector3 corner in mesh.Positions) Assert.Equal(0.0, BoxEnvelopeFitter.DistanceOutside(boxes, corner));
            for (int face = 0; face < mesh.FaceCount; face++) Assert.Equal(0.0, BoxEnvelopeFitter.DistanceOutside(boxes, mesh.FaceCentroid(face)));
        }

        [Fact]
        public void ATurnedCubeGetsItsOwnYawAndSize() {
            FacetMesh cube = Cube(new Vector3(2f, 1f, -1f), new Vector3(1f, 0.5f, 0.25f), 30);

            List<EnvelopeBox> boxes = BoxEnvelopeFitter.Fit(cube.Positions, new BoxEnvelopeSettings());

            EnvelopeBox box = Assert.Single(boxes);
            Assert.Equal(30, box.YawDegrees);
            Assert.InRange(box.HalfExtents.X, 1f, 1.003f);
            Assert.InRange(box.HalfExtents.Y, 0.5f, 0.503f);
            Assert.InRange(box.HalfExtents.Z, 0.25f, 0.253f);
            Assert.InRange(Vector3.Distance(box.Center, new Vector3(2f, 1f, -1f)), 0f, 0.001f);
        }

        [Fact]
        public void AStepPyramidKeepsItsStepsWhenTheBudgetAllows() {
            var mesh = new FacetMesh();
            mesh.Append(Cube(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 0.5f, 2f), 0));
            mesh.Append(Cube(new Vector3(0f, 1.5f, 0f), new Vector3(1f, 0.5f, 1f), 0));
            var settings = new BoxEnvelopeSettings { BandEdges = MetreBands, MaxBoxes = 2 };

            List<EnvelopeBox> boxes = BoxEnvelopeFitter.Fit(mesh.Positions, settings);

            Assert.Equal(2, boxes.Count);
            Assert.Contains(boxes, (box) => Math.Abs(box.HalfExtents.X - 2f) < 0.01f && box.Center.Y + box.HalfExtents.Y < 1.01f);
            Assert.Contains(boxes, (box) => Math.Abs(box.HalfExtents.X - 1f) < 0.01f && box.Center.Y + box.HalfExtents.Y > 1.99f);
        }

        [Fact]
        public void AMergeThatWouldRoofAKeepOutPointWaitsForAnother() {
            // A low slab beside a tower and a far second slab: with room for two boxes the cheapest merge roofs
            // the first slab with the tower, unless the air over that slab is kept out.
            var mesh = new FacetMesh();
            mesh.Append(Cube(new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.25f, 1f), 0));
            mesh.Append(Cube(new Vector3(0f, 2f, 2.1f), new Vector3(1f, 2f, 1f), 0));
            mesh.Append(Cube(new Vector3(30f, 0.25f, 0f), new Vector3(1f, 0.25f, 1f), 0));
            var keepOut = new[] { new Vector3(0f, 0.6f, 0f) };
            var free = new BoxEnvelopeSettings { MaxBoxes = 2 };
            var kept = new BoxEnvelopeSettings { MaxBoxes = 2, KeepOut = keepOut };

            List<EnvelopeBox> roofed = BoxEnvelopeFitter.Fit(mesh.Positions, free);
            List<EnvelopeBox> open = BoxEnvelopeFitter.Fit(mesh.Positions, kept);

            Assert.Equal(0.0, BoxEnvelopeFitter.DistanceOutside(roofed, keepOut[0]));
            Assert.True(BoxEnvelopeFitter.DistanceOutside(open, keepOut[0]) > 0.05);
            foreach (Vector3 corner in mesh.Positions) Assert.Equal(0.0, BoxEnvelopeFitter.DistanceOutside(open, corner));
        }

        [Fact]
        public void TheBoxCountIsAHardLimitEvenAgainstKeepOut() {
            var mesh = new FacetMesh();
            mesh.Append(Cube(new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.25f, 1f), 0));
            mesh.Append(Cube(new Vector3(0f, 2f, 2.1f), new Vector3(1f, 2f, 1f), 0));
            var settings = new BoxEnvelopeSettings { MaxBoxes = 1, KeepOut = new[] { new Vector3(0f, 0.6f, 0f) } };

            Assert.Single(BoxEnvelopeFitter.Fit(mesh.Positions, settings));
        }

        [Fact]
        public void WeightedLowVolumeSteersMergesUpward() {
            // Four pieces: two wide feet and two wide crowns. Weighting the ground band makes the crowns merge
            // with each other before a foot grows.
            var mesh = new FacetMesh();
            mesh.Append(Cube(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.5f, 1f), 0));
            mesh.Append(Cube(new Vector3(3f, 0.5f, 0f), new Vector3(1f, 0.5f, 1f), 0));
            mesh.Append(Cube(new Vector3(0f, 5f, 0f), new Vector3(1f, 0.5f, 1f), 0));
            mesh.Append(Cube(new Vector3(3f, 5.4f, 0f), new Vector3(1f, 0.5f, 1f), 0));
            var settings = new BoxEnvelopeSettings { MaxBoxes = 3, WeightedBelow = 2.0, WeightBelow = 100.0 };

            List<EnvelopeBox> boxes = BoxEnvelopeFitter.Fit(mesh.Positions, settings);

            Assert.Equal(3, boxes.Count);
            Assert.Equal(2, boxes.FindAll((box) => box.Center.Y < 1f && box.HalfExtents.X < 1.01f).Count);
        }

        [Fact]
        public void DistanceOutsideMeasuresInTheBoxFrame() {
            var box = new EnvelopeBox(new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 1f), 45);

            Assert.Equal(0.0, box.DistanceOutside(new Vector3(1f, 0.5f, 0f)));
            Assert.Equal(1.0, box.DistanceOutside(new Vector3(1f, 2f, 0f)), 5);
            float turn = (float)Math.Sqrt(0.5);
            Assert.Equal(1.0, box.DistanceOutside(new Vector3(1f + 2f * turn, 0f, -2f * turn)), 4);
            Assert.Equal(0.0, box.DistanceOutside(new Vector3(1f + (float)Math.Sqrt(2.0), 0f, 0f)), 4);
        }

        [Fact]
        public void BadSettingsThrow() {
            var corners = Cube(Vector3.Zero, Vector3.One, 0).Positions;

            Assert.Throws<ArgumentException>(() => BoxEnvelopeFitter.Fit(corners, new BoxEnvelopeSettings { MaxBoxes = 0 }));
            Assert.Throws<ArgumentException>(() => BoxEnvelopeFitter.Fit(corners, new BoxEnvelopeSettings { BandEdges = new[] { 1.0, 1.0 } }));
            Assert.Throws<ArgumentException>(() => BoxEnvelopeFitter.Fit(corners, new BoxEnvelopeSettings { YawStepDegrees = 0 }));
            Assert.Throws<ArgumentException>(() => BoxEnvelopeFitter.Fit(corners, new BoxEnvelopeSettings { WeightBelow = 0.5 }));
            Assert.Throws<ArgumentException>(() => BoxEnvelopeFitter.Fit(new[] { Vector3.Zero }, new BoxEnvelopeSettings()));
        }

        // A closed box of six quads (twelve faces) turned by whole degrees about +Y.
        private static FacetMesh Cube(Vector3 centre, Vector3 half, int yawDegrees) {
            double radians = yawDegrees * Math.PI / 180.0;
            var right = new Vector3((float)Math.Cos(radians), 0f, (float)-Math.Sin(radians));
            var forward = new Vector3((float)Math.Sin(radians), 0f, (float)Math.Cos(radians));
            Vector3 Corner(int x, int y, int z) => centre + right * (half.X * x) + Vector3.UnitY * (half.Y * y) + forward * (half.Z * z);
            var mesh = new FacetMesh(12);
            int[,] quads = { { -1, 0, 0 }, { 1, 0, 0 }, { 0, -1, 0 }, { 0, 1, 0 }, { 0, 0, -1 }, { 0, 0, 1 } };
            for (int quad = 0; quad < 6; quad++) {
                Vector3[] ring = QuadCorners(quads[quad, 0], quads[quad, 1], quads[quad, 2], Corner);
                mesh.AddFace(ring[0], ring[1], ring[2]);
                mesh.AddFace(ring[0], ring[2], ring[3]);
            }
            return mesh;
        }

        private static Vector3[] QuadCorners(int x, int y, int z, Func<int, int, int, Vector3> corner) {
            if (x != 0) return new[] { corner(x, -1, -1), corner(x, 1, -1), corner(x, 1, 1), corner(x, -1, 1) };
            if (y != 0) return new[] { corner(-1, y, -1), corner(1, y, -1), corner(1, y, 1), corner(-1, y, 1) };
            return new[] { corner(-1, -1, z), corner(1, -1, z), corner(1, 1, z), corner(-1, 1, z) };
        }
    }
}
