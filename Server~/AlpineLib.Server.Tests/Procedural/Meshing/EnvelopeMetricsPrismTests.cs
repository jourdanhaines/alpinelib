using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class EnvelopeMetricsPrismTests {
        private static readonly List<Vector3> Block = EnvelopeShapes.Cuboid(new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 1f), 0);

        [Fact]
        public void AHuggingPrismStandsOffNothingAndAGrownOneByTheGrowth() {
            var hugging = Metrics(new EnvelopePrism(0.0, 2.0, Rectangle(-1.0, 1.0, -1.0, 1.0)));
            var grown = Metrics(new EnvelopePrism(0.0, 2.0, Rectangle(-1.0, 1.5, -1.0, 1.0)));

            Assert.InRange(EnvelopeMetrics.Quantile(hugging.Standoff(0.35, 1.4, 3.0), 1.0), 0.0, 0.001);
            Assert.InRange(EnvelopeMetrics.Quantile(grown.Standoff(0.35, 1.4, 3.0), 1.0), 0.499, 0.501);
        }

        [Fact]
        public void ASlantedSideMeasuresAlongItsNormal() {
            var plan = new List<PlanPoint> { new PlanPoint(-1.0, -1.0), new PlanPoint(1.0, -1.0), new PlanPoint(2.0, 1.0), new PlanPoint(-1.0, 1.0) };

            double worst = EnvelopeMetrics.Quantile(Metrics(new EnvelopePrism(0.0, 2.0, plan)).Standoff(0.35, 1.4, 3.0), 1.0);

            Assert.InRange(worst, 0.999, 1.001);
        }

        [Fact]
        public void SidesAnotherPrismCoversAreNotExposed() {
            var halves = new[] { new EnvelopePrism(0.0, 2.0, Rectangle(-1.0, 0.0, -1.0, 1.0)), new EnvelopePrism(0.0, 2.0, Rectangle(0.0, 1.0, -1.0, 1.0)) };
            var stacked = new[] { new EnvelopePrism(0.0, 1.0, Rectangle(-1.0, 1.0, -1.0, 1.0)), new EnvelopePrism(1.0, 2.0, Rectangle(-1.0, 1.0, -1.0, 1.0)) };

            Assert.InRange(EnvelopeMetrics.Quantile(Metrics(halves).Standoff(0.35, 1.4, 3.0), 1.0), 0.0, 0.001);
            Assert.InRange(EnvelopeMetrics.Quantile(Metrics(stacked).Standoff(0.35, 1.4, 3.0), 1.0), 0.0, 0.001);
            Assert.InRange(EnvelopeMetrics.Quantile(Metrics(stacked).LedgeAir(0.0, 0.3, 1.5), 1.0), 0.0, 0.0);
        }

        [Fact]
        public void APrismCapOverTheLookCountsItsAir() {
            var raised = Metrics(new EnvelopePrism(0.0, 2.4, Rectangle(-1.0, 1.0, -1.0, 1.0)));
            var wide = Metrics(new EnvelopePrism(0.0, 2.0, Rectangle(-1.0, 1.5, -1.0, 1.0)));

            Assert.InRange(EnvelopeMetrics.Quantile(raised.LedgeAir(0.0, 0.3, 3.0), 1.0), 0.399, 0.401);
            Assert.InRange(EnvelopeMetrics.Quantile(wide.LedgeAir(0.0, 0.3, 3.0), 1.0), 1.999, 2.001);
        }

        [Fact]
        public void APrismMeasuresDistanceOutside() {
            var prism = new EnvelopePrism(0.0, 2.0, Rectangle(-1.0, 1.0, -1.0, 1.0));

            Assert.Equal(0.0, prism.DistanceOutside(new Vector3(1f, 2f, 0f)));
            Assert.Equal(0.5, prism.DistanceOutside(new Vector3(1.5f, 1f, 0f)), 6);
            Assert.Equal(0.5, prism.DistanceOutside(new Vector3(0f, -0.5f, 0f)), 6);
            Assert.Equal(8.0, prism.Volume, 9);
        }

        private static EnvelopeMetrics Metrics(params EnvelopePrism[] prisms) {
            return new EnvelopeMetrics(Block, null, null, prisms, 0.1);
        }

        private static List<PlanPoint> Rectangle(double minX, double maxX, double minZ, double maxZ) {
            return new List<PlanPoint> { new PlanPoint(minX, minZ), new PlanPoint(maxX, minZ), new PlanPoint(maxX, maxZ), new PlanPoint(minX, maxZ) };
        }
    }
}
