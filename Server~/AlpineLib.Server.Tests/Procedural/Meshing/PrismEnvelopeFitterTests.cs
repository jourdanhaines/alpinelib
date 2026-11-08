using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Procedural.Meshing;
using Xunit;
using Xunit.Abstractions;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class PrismEnvelopeFitterTests {
        private const double Lean = 0.16;
        private readonly ITestOutputHelper _output;

        public PrismEnvelopeFitterTests(ITestOutputHelper output) {
            _output = output;
        }

        [Fact]
        public void ALeaningLoftStandsOffLittle() {
            List<Vector3> look = LeaningLoft();

            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings());
            _output.WriteLine(fit.Report);

            Assert.True(fit.MeetsTargets);
            Assert.Equal(0, fit.FallbackCount);
            Assert.Contains("rectangle fallbacks 0", fit.Report);
            Assert.InRange(fit.StandoffP90, 0.0, 0.3);
            Assert.InRange(fit.Prisms.Count, 1, 24);
            Assert.InRange(fit.WorstOutside, 0.0, 0.05);
        }

        [Fact]
        public void AStrongLeanSplitsColumnsInHeight() {
            List<Vector3> look = LeaningLoft(0.8);

            PrismEnvelopeFit halved = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings { Strict = false });
            PrismEnvelopeFit whole = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings { MinBand = 1.0, Strict = false });
            _output.WriteLine(halved.Report);
            _output.WriteLine(whole.Report);

            Assert.True(halved.BandEdges.Count > whole.BandEdges.Count);
            Assert.True(halved.StandoffP90 < whole.StandoffP90);
            Assert.InRange(halved.Prisms.Count, 1, 24);
        }

        [Fact]
        public void TheBudgetCapsThePrismCount() {
            var look = new List<Vector3>();
            for (int pillar = 0; pillar < 6; pillar++) look.AddRange(EnvelopeShapes.Cuboid(new Vector3(pillar * 2f, 1.5f, (pillar % 2) * 2f), new Vector3(0.4f, 1.5f, 0.4f), pillar * 10));

            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings { MaxPrisms = 10, Strict = false });
            _output.WriteLine(fit.Report);

            Assert.InRange(fit.Prisms.Count, 1, 10);
            Assert.InRange(fit.WorstOutside, 0.0, 0.05);
            Assert.False(fit.MeetsTargets);
            InvalidOperationException strict = Assert.Throws<InvalidOperationException>(() => PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings { MaxPrisms = 10 }));
            Assert.Contains("standoff p90", strict.Message);
        }

        [Fact]
        public void ABudgetBelowTheBandCountIsRefused() {
            var settings = new PrismEnvelopeSettings { MaxPrisms = 2 };

            Assert.Equal(3, settings.MinimumBands);
            Assert.Throws<ArgumentException>(() => PrismEnvelopeFitter.Fit(LeaningLoft(), settings));
            for (int budget = 3; budget <= 6; budget++) {
                PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(TwoLobes(), new PrismEnvelopeSettings { MaxPrisms = budget, Strict = false });
                Assert.InRange(fit.Prisms.Count, 1, budget);
            }
        }

        [Fact]
        public void TheFeetCapSitsUnderTheMotorsRealStep() {
            Assert.Equal(0.3 - 20.0 / 900.0 - 0.01, new PrismEnvelopeSettings().FeetTop, 12);
            Assert.Equal(0.2678, new PrismEnvelopeSettings().FeetTop, 4);
            Assert.Equal(0.3 - 20.0 / 3600.0 - 0.01, new PrismEnvelopeSettings { TickSeconds = 1.0 / 60.0 }.FeetTop, 9);
            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(LeaningLoft(), new PrismEnvelopeSettings());
            Assert.Equal(0.2678, fit.BandEdges[1], 9);
        }

        [Fact]
        public void TwoLobesGetSeparatePrismsInTheWalkingBand() {
            List<Vector3> look = TwoLobes();

            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings());
            _output.WriteLine(fit.Report);

            int walking = 0;
            foreach (EnvelopePrism prism in fit.Prisms) {
                if (prism.Bottom <= 1.0 && prism.Top >= 1.0) walking++;
            }
            Assert.True(walking >= 2, fit.Report);
            Assert.InRange(fit.StandoffP90, 0.0, 0.3);
            Assert.InRange(fit.Prisms.Count, 1, 24);
        }

        [Fact]
        public void EveryLookSampleIsInsideAPrismAsACollisionShape() {
            foreach (List<Vector3> look in new[] { LeaningLoft(), TwoLobes() }) {
                PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings());
                var shapes = new List<CollisionShape>();
                foreach (EnvelopePrism prism in fit.Prisms) shapes.Add(ToShape(prism, 0.0, 1.0));

                for (int face = 0; face + 2 < look.Count; face += 3) {
                    Vector3 centroid = (look[face] + look[face + 1] + look[face + 2]) / 3f;
                    Assert.InRange(CollisionShapeDistance.Outside(shapes, 0, shapes.Count, centroid), 0.0, 0.05);
                    for (int corner = 0; corner < 3; corner++) Assert.InRange(CollisionShapeDistance.Outside(shapes, 0, shapes.Count, look[face + corner]), 0.0, 0.05);
                }
            }
        }

        [Fact]
        public void FitsSurviveAnyTurnScaleAndRounding() {
            foreach (List<Vector3> look in new[] { LeaningLoft(), TwoLobes() }) {
                PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings());
                foreach (EnvelopePrism prism in fit.Prisms) {
                    Assert.InRange(prism.Plan.Count, 3, 16);
                    for (int yaw = 0; yaw < 360; yaw++) {
                        ToShape(prism, yaw, 0.7);
                        ToShape(prism, yaw, 1.4);
                    }
                }
            }
        }

        [Fact]
        public void ReRunsAreBitIdentical() {
            PrismEnvelopeFit first = PrismEnvelopeFitter.Fit(LeaningLoft(), new PrismEnvelopeSettings());
            PrismEnvelopeFit second = PrismEnvelopeFitter.Fit(LeaningLoft(), new PrismEnvelopeSettings());

            Assert.Equal(first.Prisms, second.Prisms);
            Assert.Equal(first.BandEdges, second.BandEdges);
        }

        [Fact]
        public void BandsFollowTheSettings() {
            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(EnvelopeShapes.Cuboid(new Vector3(0f, 1.4f, 0f), new Vector3(1f, 1.6f, 1f), 30), new PrismEnvelopeSettings { MaxScale = 2.0 });

            Assert.Equal(-0.2, fit.BandEdges[0], 3);
            Assert.Equal(0.1339, fit.BandEdges[1], 9);
            Assert.Equal(3.0, fit.BandEdges[fit.BandEdges.Count - 1], 9);
            Assert.Contains(fit.BandEdges, (edge) => Math.Abs(edge - 1.125) < 1e-9);
            Assert.InRange(fit.StandoffP90, 0.0, 0.01);
        }

        [Fact]
        public void BadSettingsAreRefused() {
            Assert.Throws<ArgumentException>(() => PrismEnvelopeFitter.Fit(LeaningLoft(), new PrismEnvelopeSettings { MaxVertices = 17 }));
            Assert.Throws<ArgumentException>(() => PrismEnvelopeFitter.Fit(LeaningLoft(), new PrismEnvelopeSettings { FineEdges = new[] { 1.3, 0.8 } }));
            Assert.Throws<ArgumentException>(() => PrismEnvelopeFitter.Fit(new List<Vector3> { Vector3.Zero }, new PrismEnvelopeSettings()));
        }

        [Fact]
        public void ATwentyMetreRoundedLobeMeetsTargetsWithFewPrisms() {
            var ring = new List<Vector3>();
            for (int index = 0; index < 40; index++) {
                double angle = 2.0 * Math.PI * index / 40.0;
                ring.Add(new Vector3((float)(10.0 * Math.Cos(angle)), 0f, (float)(7.0 * Math.Sin(angle))));
            }
            List<Vector3> look = EnvelopeShapes.Prism(ring, -0.2f, 6f, Vector3.Zero);

            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings { MaxPrisms = 12 });
            _output.WriteLine(fit.Report);

            Assert.True(fit.MeetsTargets);
            Assert.InRange(fit.StandoffP90, 0.0, 0.3);
            Assert.InRange(fit.Prisms.Count, 1, 12);
        }

        [Fact]
        public void CapsDoNotBridgeANotchOverSand() {
            var ring = new List<Vector3> {
                new Vector3(-2f, 0f, -2f), new Vector3(2f, 0f, -2f), new Vector3(2f, 0f, 2f), new Vector3(0.3f, 0f, 2f),
                new Vector3(0f, 0f, 0.2f), new Vector3(-0.3f, 0f, 2f), new Vector3(-2f, 0f, 2f),
            };
            List<Vector3> look = EnvelopeShapes.Prism(ring, -0.2f, 1.1f, Vector3.Zero);

            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings());
            _output.WriteLine(fit.Report);

            Assert.True(fit.MeetsTargets, fit.Report);
            Assert.InRange(fit.LedgeAirMax, 0.0, 0.35);
            Assert.True(fit.Prisms.Count >= 2);
        }

        [Fact]
        public void TheBudgetRunsOutAboveTheWalkingBandFirst() {
            var calm = new List<Vector3>();
            for (int index = 0; index < 24; index++) {
                double angle = 2.0 * Math.PI * index / 24.0;
                calm.Add(new Vector3((float)(2.0 * Math.Cos(angle)), 0f, (float)(2.0 * Math.Sin(angle))));
            }
            List<Vector3> look = EnvelopeShapes.Prism(calm, -0.2f, 2.2f, Vector3.Zero);
            look.AddRange(EnvelopeShapes.Prism(EnvelopeShapes.NoisyRing(24, 3f, 0.4f, 9), 2.4f, 3.4f, Vector3.Zero));

            PrismEnvelopeFit fit = PrismEnvelopeFitter.Fit(look, new PrismEnvelopeSettings { MaxPrisms = 4, Strict = false });
            _output.WriteLine(fit.Report);

            Assert.InRange(fit.StandoffP90, 0.0, 0.3);
            Assert.InRange(fit.StandoffMax, 0.0, 0.5);
            Assert.InRange(fit.Prisms.Count, 1, 4);
        }

        // A capped 12-sided column 4 m tall, sunk 0.2 m, its rings leaning 0.16 m per metre along +x.
        private static List<Vector3> LeaningLoft() {
            return LeaningLoft(Lean);
        }

        private static List<Vector3> LeaningLoft(double lean) {
            Vector2[] polygon = LoftMesh.JitterPolygon(7UL, 12, 1.5f, 1.0f, 0.15f, false);
            var rings = new List<LoftRing>();
            for (int ring = 0; ring <= 17; ring++) {
                float height = -0.2f + ring * 0.25f;
                var shifted = new Vector2[polygon.Length];
                for (int index = 0; index < polygon.Length; index++) shifted[index] = polygon[index] + new Vector2((float)(lean * height), 0f);
                rings.Add(new LoftRing(height, shifted));
            }
            return LoftMesh.Build(rings, true, true).Positions;
        }

        // Two boxes meeting in an L, 2.5 m tall.
        private static List<Vector3> TwoLobes() {
            List<Vector3> look = EnvelopeShapes.Cuboid(new Vector3(0f, 1.25f, 0f), new Vector3(2f, 1.25f, 0.5f), 0);
            look.AddRange(EnvelopeShapes.Cuboid(new Vector3(1.5f, 1.25f, 2f), new Vector3(0.5f, 1.25f, 2f), 0));
            return look;
        }

        // The prism turned by whole degrees and scaled, its offsets rounded to 1e-4 like an exporter would.
        private static CollisionShape ToShape(EnvelopePrism prism, double yawDegrees, double scale) {
            double radians = yawDegrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians), sine = Math.Sin(radians);
            var offsets = new Vector2[prism.Plan.Count];
            for (int index = 0; index < offsets.Length; index++) {
                double x = prism.Plan[index].X * scale, z = prism.Plan[index].Z * scale;
                offsets[index] = new Vector2((float)RoundTo(x * cosine + z * sine), (float)RoundTo(-x * sine + z * cosine));
            }
            float half = (float)((prism.Top - prism.Bottom) * scale * 0.5);
            return CollisionShape.MakePrism(new Vector3(0f, (float)((prism.Top + prism.Bottom) * scale * 0.5), 0f), half, offsets);
        }

        private static double RoundTo(double value) {
            return Math.Round(value * 10000.0) / 10000.0;
        }
    }
}
