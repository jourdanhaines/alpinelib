using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    /// <summary>
    /// One hash per deterministic entry point over fixed inputs, for a Mono (editor) smoke test to recompute and compare:
    /// each mixes every output double's bits with <see cref="StableHash.Mix"/> from the FNV offset basis.
    /// </summary>
    public sealed class ParityProbeTests {
        private static readonly PolygonFilletSettings FilletSettings = new PolygonFilletSettings(0.8660254037844386, 0.45);
        private static readonly TalusApronSettings TalusSettings = new TalusApronSettings(0.3, 0.1, 0.70020753820970982, 1.01, 2, 10, 0.5, 1.0, 1.5, 0.02);
        private static readonly LooseSeatSettings SeatSettings = new LooseSeatSettings(7, new[] { -0.6, -0.2, 0.2, 0.6 }, 0.15);

        [Fact]
        public void DegreeSineHash() {
            ulong hash = GoldenHash.Start;
            for (int degrees = -360; degrees < 720; degrees++) {
                DegreeSine.SinCos(degrees, out double sine, out double cosine);
                hash = GoldenHash.Add(GoldenHash.Add(hash, sine), cosine);
            }
            Assert.Equal(0x338B3E16FDB92C6AUL, hash);
        }

        [Fact]
        public void DegreeRotationHash() {
            ulong hash = GoldenHash.Start;
            foreach (int yaw in new[] { -370, 0, 37, 90, 211, 359 }) {
                foreach (int pitch in new[] { -45, 0, 15, 90 }) {
                    foreach (int roll in new[] { -30, 0, 60 }) hash = AddRotation(hash, new DegreeRotation(yaw, pitch, roll));
                }
            }
            Assert.Equal(0xFB78CF4C7932766BUL, hash);
        }

        [Fact]
        public void PolygonFilletHash() {
            ulong hash = GoldenHash.Start;
            List<PlanPoint> square = Square(10.0);
            List<PlanPoint> hexagon = Hexagon(30.0);
            hash = AddFillet(hash, square, Corners(square.Count));
            hash = AddFillet(hash, hexagon, Corners(hexagon.Count));
            Assert.Equal(0x773EA812DFDC9370UL, hash);
        }

        [Fact]
        public void TalusApronHash() {
            var none = new List<IReadOnlyList<PlanPoint>>();
            List<PlanPoint> hexagon = Hexagon(30.0);
            ulong hash = GoldenHash.Start;
            hash = GoldenHash.Add(hash, (long)GoldenHash.Result(TalusApron.Lay(hexagon, none, 6.0, 0.57735026918962576, (edge) => false, none, (piece) => true, TalusSettings)));
            hash = GoldenHash.Add(hash, (long)GoldenHash.Result(TalusApron.Lay(hexagon, none, 6.0, 0.66188248591897087, (edge) => edge == 2, none, (piece) => true, TalusSettings)));
            hash = GoldenHash.Add(hash, (long)GoldenHash.Result(TalusApron.Lay(SeamFoot(), none, 4.4, 0.6075, (edge) => edge == 3 || edge == 5, none, (piece) => true, TalusSettings)));
            Assert.Equal(0xD99BF6DE266DD5EEUL, hash);
        }

        [Fact]
        public void LooseSeatHash() {
            Func<double, double, double> slope = (x, z) => 0.6 * x - 0.2 * z + 4.0;
            ulong hash = GoldenHash.Start;
            foreach ((int yaw, int pitch, int roll) in new[] { (45, 15, 0), (200, -25, 30), (90, 40, -40) }) {
                var rotation = new DegreeRotation(yaw, pitch, roll);
                double seated = LooseSeat.Seated(rotation, 2.0, 50.0, -1.0, 1.2, 0.8, 0.9, slope, SeatSettings);
                hash = GoldenHash.Add(GoldenHash.Add(hash, seated), LooseSeat.Buried(rotation, 2.0, seated, -1.0, 1.2, 0.8, 0.9, slope));
            }
            Assert.Equal(0x186A718AF443F58AUL, hash);
        }

        private static ulong AddRotation(ulong hash, DegreeRotation rotation) {
            rotation.Apply(1.25, -0.5, 2.0, out double x, out double y, out double z);
            rotation.Extents(1.5, 0.8, 2.2, out double extentX, out double extentY, out double extentZ);
            hash = GoldenHash.Add(GoldenHash.Add(GoldenHash.Add(hash, x), y), z);
            hash = GoldenHash.Add(GoldenHash.Add(GoldenHash.Add(hash, extentX), extentY), extentZ);
            return GoldenHash.Add(hash, rotation.RoundedExtentY(1.5, 0.8, 2.2));
        }

        private static ulong AddFillet(ulong hash, List<PlanPoint> polygon, List<FilletCorner> corners) {
            hash = GoldenHash.Add(hash, PolygonFillet.Round(polygon, corners, FilletSettings));
            for (int index = 0; index < polygon.Count; index++) hash = GoldenHash.Add(hash, PolygonFillet.MaxRadius(polygon, index, 0.5));
            return hash;
        }

        // Even corners radius 3.5 with a midpoint, odd ones 2.5 without.
        private static List<FilletCorner> Corners(int count) {
            var corners = new List<FilletCorner>(count);
            for (int index = 0; index < count; index++) corners.Add(index % 2 == 0 ? new FilletCorner(3.5, true) : new FilletCorner(2.5, false));
            return corners;
        }

        private static List<PlanPoint> Square(double size) {
            return new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(size, 0), new PlanPoint(size, size), new PlanPoint(0, size) };
        }

        // Literal corners: (r, 0), (r/2, a), (−r/2, a), (−r, 0), (−r/2, −a), (r/2, −a), a = r·0.8660254037844386 rounded to 1e-4.
        private static List<PlanPoint> Hexagon(double radius) {
            double half = radius * 0.5, across = ConvexPlan.Round(radius * 0.8660254037844386);
            return new List<PlanPoint> {
                new PlanPoint(radius, 0), new PlanPoint(half, across), new PlanPoint(-half, across), new PlanPoint(-radius, 0), new PlanPoint(-half, -across), new PlanPoint(half, -across),
            };
        }

        private static List<PlanPoint> SeamFoot() {
            return new List<PlanPoint> {
                new PlanPoint(1.1, 2.6), new PlanPoint(22.3, 2.7), new PlanPoint(25.6, 4.0), new PlanPoint(23.8, 9.0), new PlanPoint(14.9, 11.4), new PlanPoint(8.6, 8.2),
            };
        }
    }
}
