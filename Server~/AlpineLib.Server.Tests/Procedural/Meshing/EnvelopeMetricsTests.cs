using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class EnvelopeMetricsTests {
        private static readonly List<Vector3> Block = EnvelopeShapes.Cuboid(new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 1f), 0);

        [Fact]
        public void AHuggingBoxStandsOffNothingAndAGrownOneByTheGrowth() {
            var hugging = new EnvelopeMetrics(Block, new[] { new EnvelopeBox(new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 1f), 0) }, null, 0.1);
            var grown = new EnvelopeMetrics(Block, new[] { new EnvelopeBox(new Vector3(0f, 1f, 0f), new Vector3(1.5f, 1f, 1f), 0) }, null, 0.1);

            Assert.InRange(EnvelopeMetrics.Quantile(hugging.Standoff(0.35, 1.4, 3.0), 1.0), 0.0, 0.001);
            Assert.InRange(EnvelopeMetrics.Quantile(grown.Standoff(0.35, 1.4, 3.0), 1.0), 0.499, 0.501);
        }

        [Fact]
        public void FacesTwoBoxesShareAreNotExposed() {
            var halves = new[] { new EnvelopeBox(new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 1f), 0), new EnvelopeBox(new Vector3(0.5f, 1f, 0f), new Vector3(0.5f, 1f, 1f), 0) };

            Assert.InRange(EnvelopeMetrics.Quantile(new EnvelopeMetrics(Block, halves, null, 0.1).Standoff(0.35, 1.4, 3.0), 1.0), 0.0, 0.001);
        }

        [Fact]
        public void ABoxTopOverTheLookCountsItsAir() {
            var raised = new EnvelopeMetrics(Block, new[] { new EnvelopeBox(new Vector3(0f, 1.2f, 0f), new Vector3(1f, 1.2f, 1f), 0) }, null, 0.1);
            var wide = new EnvelopeMetrics(Block, new[] { new EnvelopeBox(new Vector3(0f, 1f, 0f), new Vector3(1.5f, 1f, 1f), 0) }, null, 0.1);

            Assert.InRange(EnvelopeMetrics.Quantile(raised.LedgeAir(0.0, 0.3, 3.0), 1.0), 0.399, 0.401);
            Assert.InRange(EnvelopeMetrics.Quantile(wide.LedgeAir(0.0, 0.3, 3.0), 1.0), 1.999, 2.001);
        }

        [Fact]
        public void ASphereCapCountsItsAirAndItsSurfaceItsStandoff() {
            var sphere = new EnvelopeMetrics(Block, null, new[] { new EnvelopeSphere(new Vector3(0f, 1f, 0f), 1.8f) }, 0.1);

            List<double> air = sphere.LedgeAir(0.0, 0.3, 3.0);
            Assert.Contains(air, (value) => value > 0.79 && value < 0.81);
            Assert.True(EnvelopeMetrics.Quantile(air, 1.0) > 2.0);
            Assert.True(EnvelopeMetrics.Quantile(sphere.Standoff(0.35, 1.4, 3.0), 1.0) > 0.5);
        }
    }
}
