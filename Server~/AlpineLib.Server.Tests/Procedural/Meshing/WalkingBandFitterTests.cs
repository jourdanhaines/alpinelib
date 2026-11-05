using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class WalkingBandFitterTests {
        private static WalkingBandSettings Settings() {
            return new WalkingBandSettings {
                Ground = 0.0, Step = 0.3, Top = 2.25, WallHeight = 0.3, Cell = 0.25, Reach = 0.4, FootReach = 1.0, YawStepDegrees = 10,
                UpperBandEdges = new[] { 4.0, 6.0 }, UpperMaxBoxes = 2,
            };
        }

        // A lobed wall 8 m tall standing in a low 0.2 m apron ring.
        private static List<Vector3> Mesa() {
            var look = EnvelopeShapes.Prism(EnvelopeShapes.NoisyRing(24, 6f, 0.15f, 3), -0.2f, 8f, Vector3.Zero);
            look.AddRange(EnvelopeShapes.Prism(EnvelopeShapes.NoisyRing(24, 8f, 0.05f, 4), -0.2f, 0.2f, Vector3.Zero));
            return look;
        }

        [Fact]
        public void TheBoxesHoldTheWholeLook() {
            List<Vector3> look = Mesa();

            List<EnvelopeBox> boxes = WalkingBandFitter.Fit(look, Settings()).All();

            for (int face = 0; face + 2 < look.Count; face += 3) {
                Vector3 centroid = (look[face] + look[face + 1] + look[face + 2]) / 3f;
                Assert.Equal(0.0, BoxEnvelopeFitter.DistanceOutside(boxes, centroid));
                for (int corner = 0; corner < 3; corner++) Assert.Equal(0.0, BoxEnvelopeFitter.DistanceOutside(boxes, look[face + corner]));
            }
        }

        [Fact]
        public void ColumnsReachTheBandTopFeetStayUnderTheStepAndNothingFloats() {
            List<Vector3> look = Mesa();
            WalkingBandSettings settings = Settings();

            WalkingBandFit fit = WalkingBandFitter.Fit(look, settings);

            Assert.NotEmpty(fit.Columns);
            Assert.NotEmpty(fit.Feet);
            Assert.All(fit.Columns, (box) => Assert.True(box.Center.Y + box.HalfExtents.Y >= settings.Top));
            Assert.All(fit.Feet, (box) => Assert.True(box.Center.Y + box.HalfExtents.Y <= settings.Step + 0.01));
            var metrics = new EnvelopeMetrics(look, fit.All(), null, 0.2);
            Assert.True(EnvelopeMetrics.Quantile(metrics.LedgeAir(settings.Ground, settings.Step + 0.01, 2.2), 1.0) <= settings.LedgeTolerance + 0.01);
        }

        [Fact]
        public void ColumnsStandNoFurtherOffTheWallThanTheReachAndACell() {
            List<Vector3> look = Mesa();
            WalkingBandSettings settings = Settings();

            WalkingBandFit fit = WalkingBandFitter.Fit(look, settings);
            var metrics = new EnvelopeMetrics(look, fit.All(), null, 0.2);
            List<double> standoff = metrics.Standoff(0.35, 1.4, 5.0);

            Assert.NotEmpty(standoff);
            Assert.InRange(EnvelopeMetrics.Quantile(standoff, 1.0), 0.0, settings.Reach + settings.Cell * Math.Sqrt(2.0) + 0.01);
        }

        [Fact]
        public void AFlatTopBelowTheBandTopGetsAFlatStepNotAColumn() {
            List<Vector3> look = EnvelopeShapes.Cuboid(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 0.5f, 1.5f), 20);
            WalkingBandSettings settings = Settings();
            settings.WallHeight = settings.Top;

            WalkingBandFit fit = WalkingBandFitter.Fit(look, settings);

            Assert.Empty(fit.Columns);
            Assert.NotEmpty(fit.Steps);
            Assert.All(fit.Steps, (box) => Assert.InRange(box.Center.Y + box.HalfExtents.Y, 1.0, 1.01));
        }

        [Fact]
        public void TheFitIsDeterministic() {
            List<EnvelopeBox> first = WalkingBandFitter.Fit(Mesa(), Settings()).All();
            List<EnvelopeBox> second = WalkingBandFitter.Fit(Mesa(), Settings()).All();

            Assert.Equal(first, second);
        }

        [Fact]
        public void BadSettingsAreRefused() {
            WalkingBandSettings settings = Settings();
            settings.Top = settings.Step;

            Assert.Throws<ArgumentException>(() => WalkingBandFitter.Fit(Mesa(), settings));
        }
    }
}
