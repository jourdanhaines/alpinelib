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
        public void ASlopedTopCountsItsAirAndCapsItsSides() {
            var sloped = Metrics(EnvelopePrism.Sloped(0.0, Rectangle(-1.0, 1.0, -1.0, 1.0), 2.25, 0.25, 0.0));

            Assert.InRange(EnvelopeMetrics.Quantile(sloped.LedgeAir(0.0, 0.3, 3.0), 1.0), 0.499, 0.501);
            Assert.InRange(EnvelopeMetrics.Quantile(sloped.LedgeAir(0.0, 0.3, 3.0), 0.0), 0.0, 0.02);
            Assert.InRange(EnvelopeMetrics.Quantile(sloped.Standoff(0.35, 1.4, 3.0), 1.0), 0.0, 0.001);
            Assert.InRange(EnvelopeMetrics.Quantile(sloped.Standoff(0.35, 3.0, 3.0), 1.0), 0.499, 0.501);
        }

        [Fact]
        public void ASlopedPrismMeasuresToItsPlaneAndCopiesTheShape() {
            EnvelopePrism sloped = EnvelopePrism.Sloped(0.0, Rectangle(-1.0, 1.0, -1.0, 1.0), 2.0, 0.75, 0.0);

            Assert.True(sloped.IsSloped);
            Assert.Equal(2.75, sloped.Top);
            Assert.Equal(1.25, sloped.TopAt(-1.0, 0.0));
            Assert.Equal(0.0, sloped.DistanceOutside(0.5, 2.3, 0.0));
            Assert.Equal(0.8, sloped.DistanceOutside(0.0, 3.0, 0.0), 9);
            Assert.True(sloped.Holds(0.0, 2.0, 0.0, 0.0));
            Assert.False(sloped.Holds(-0.9, 2.0, 0.0, 0.1));
            Assert.Equal(8.0, sloped.Volume, 9);
            Assert.Equal(sloped, EnvelopePrism.Sloped(0.0, Rectangle(-1.0, 1.0, -1.0, 1.0), 2.0, 0.75, 0.0));
            Assert.NotEqual(sloped, EnvelopePrism.Sloped(0.0, Rectangle(-1.0, 1.0, -1.0, 1.0), 2.0, 0.5, 0.0));
            Assert.Throws<System.ArgumentException>(() => EnvelopePrism.Sloped(1.3, Rectangle(-1.0, 1.0, -1.0, 1.0), 2.0, 0.75, 0.0));
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
        public void ALipHidesOnlyAgainstAPrismRisingAboveTheCap() {
            var strip = new EnvelopePrism(0.0, 1.0, Rectangle(1.0, 1.2, -1.0, 1.0));
            var level = new EnvelopePrism(0.0, 1.0, Rectangle(-1.0, 1.0, -1.0, 1.0));
            var wall = new EnvelopePrism(0.0, 2.0, Rectangle(-1.0, 1.0, -1.0, 1.0));
            var thin = new EnvelopePrism(0.0, 1.0, Rectangle(1.0, 1.05, -1.0, 1.0));

            var besideLevel = new EnvelopeMetrics(Block, null, null, new[] { level, strip }, 0.05) { LedgeLip = 0.1 };
            var besideWall = new EnvelopeMetrics(Block, null, null, new[] { wall, strip }, 0.05) { LedgeLip = 0.1 };
            var thinBesideWall = new EnvelopeMetrics(Block, null, null, new[] { wall, thin }, 0.05) { LedgeLip = 0.1 };

            Assert.InRange(EnvelopeMetrics.Quantile(besideLevel.LedgeAir(0.0, 0.3, 1.5), 1.0), 0.999, 1.001);
            List<double> partly = besideLevel.LedgeAir(0.0, 0.3, 1.5);
            Assert.True(partly.FindAll((air) => air > 0.99).Count > besideWall.LedgeAir(0.0, 0.3, 1.5).FindAll((air) => air > 0.99).Count);
            Assert.InRange(EnvelopeMetrics.Quantile(besideWall.LedgeAir(0.0, 0.3, 1.5), 1.0), 0.999, 1.001);
            Assert.Empty(thinBesideWall.LedgeAir(0.0, 0.3, 1.5));
        }

        [Fact]
        public void ALipNeedsItsNeighbourToRiseEnough() {
            var thin = new EnvelopePrism(0.0, 1.0, Rectangle(1.0, 1.05, -1.0, 1.0));
            var step = new EnvelopePrism(0.0, 1.1, Rectangle(-1.0, 1.0, -1.0, 1.0));
            var wall = new EnvelopePrism(0.0, 1.2, Rectangle(-1.0, 1.0, -1.0, 1.0));
            var hovering = new EnvelopePrism(1.1, 2.0, Rectangle(-1.0, 1.0, -1.0, 1.0));
            var farAbove = new EnvelopePrism(1.5, 2.5, Rectangle(-1.0, 1.0, -1.0, 1.0));

            // The look stands well away, so only neighbouring prisms can make the strip a lip, never a wall top.
            List<Vector3> away = EnvelopeShapes.Cuboid(new Vector3(10f, 1f, 0f), new Vector3(1f, 1f, 1f), 0);
            var besideStep = new EnvelopeMetrics(away, null, null, new[] { step, thin }, 0.05) { LedgeLip = 0.1, LipRise = 0.15 };
            var besideWall = new EnvelopeMetrics(away, null, null, new[] { wall, thin }, 0.05) { LedgeLip = 0.1, LipRise = 0.15 };
            var underHovering = new EnvelopeMetrics(away, null, null, new[] { step, hovering, thin }, 0.05) { LedgeLip = 0.1, LipRise = 0.15 };
            var underFar = new EnvelopeMetrics(away, null, null, new[] { step, farAbove, thin }, 0.05) { LedgeLip = 0.1, LipRise = 0.15 };

            Assert.Contains(besideStep.LedgeAir(0.0, 0.3, 1.05), StripAir);
            Assert.DoesNotContain(besideWall.LedgeAir(0.0, 0.3, 1.05), StripAir);
            Assert.DoesNotContain(underHovering.LedgeAir(0.0, 0.3, 1.05), StripAir);
            Assert.Contains(underFar.LedgeAir(0.0, 0.3, 1.05), StripAir);
        }

        [Fact]
        public void SidesBuriedInTheLookAreNoStandoff() {
            var buried = Metrics(new EnvelopePrism(0.0, 2.0, Rectangle(-0.5, 0.5, -0.5, 0.5)));

            Assert.Empty(buried.Standoff(0.35, 1.4, 3.0));
        }

        [Fact]
        public void APrismMeasuresDistanceOutside() {
            var prism = new EnvelopePrism(0.0, 2.0, Rectangle(-1.0, 1.0, -1.0, 1.0));

            Assert.Equal(0.0, prism.DistanceOutside(new Vector3(1f, 2f, 0f)));
            Assert.Equal(0.5, prism.DistanceOutside(new Vector3(1.5f, 1f, 0f)), 6);
            Assert.Equal(0.5, prism.DistanceOutside(new Vector3(0f, -0.5f, 0f)), 6);
            Assert.Equal(8.0, prism.Volume, 9);
        }

        // The thin strip's cap at 1.0 over open ground.
        private static bool StripAir(double air) {
            return air > 0.999 && air < 1.001;
        }

        private static EnvelopeMetrics Metrics(params EnvelopePrism[] prisms) {
            return new EnvelopeMetrics(Block, null, null, prisms, 0.1);
        }

        private static List<PlanPoint> Rectangle(double minX, double maxX, double minZ, double maxZ) {
            return new List<PlanPoint> { new PlanPoint(minX, minZ), new PlanPoint(maxX, minZ), new PlanPoint(maxX, maxZ), new PlanPoint(minX, maxZ) };
        }
    }
}
