using System;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class FarGroundSettingsTests {
        private const double CameraFarClip = 2200.0;

        [Fact]
        public void DefaultsValidateAndReachPastTheFarClipWhereverTheAnchorSits() {
            FarGroundSettings settings = FarGroundSettings.Default;
            settings.Validate();

            Assert.True(settings.GuaranteedReach >= CameraFarClip, $"reach {settings.GuaranteedReach} < {CameraFarClip}");
            Assert.Equal(settings.OuterRadius - settings.Snap * Math.Sqrt(0.5), settings.GuaranteedReach, 12);
        }

        [Fact]
        public void HoleIsTheInnerRadiusLessHalfASnapDiagonal() {
            var settings = new FarGroundSettings { InnerRadius = 400.0, Snap = 48.0, Spacing = 16.0 };

            Assert.Equal(400.0 - 48.0 * Math.Sqrt(0.5), settings.HoleRadius, 12);
            Assert.Equal(48.0 * Math.Sqrt(0.5), settings.SnapSlack, 12);

            // The farthest snapped anchor (a snap cell's corner) still leaves the inner radius about the camera to the world.
            double anchorOffset = Math.Sqrt(2.0) * settings.Snap * 0.5;
            Assert.True(settings.HoleRadius + anchorOffset <= settings.InnerRadius + 1e-9);
        }

        [Theory]
        [InlineData(0.0, 32.0, 380.0, 2240.0, 0.3, "Spacing")]
        [InlineData(16.0, 0.0, 380.0, 2240.0, 0.3, "Snap")]
        [InlineData(16.0, 24.0, 380.0, 2240.0, 0.3, "Snap")]
        [InlineData(16.0, 32.0, 0.0, 2240.0, 0.3, "InnerRadius")]
        [InlineData(16.0, 32.0, 20.0, 2240.0, 0.3, "HoleRadius")]
        [InlineData(16.0, 32.0, 380.0, 380.0, 0.3, "OuterRadius")]
        [InlineData(16.0, 32.0, 380.0, double.PositiveInfinity, 0.3, "OuterRadius")]
        [InlineData(16.0, 32.0, 380.0, 2240.0, -0.1, "Drop")]
        [InlineData(16.0, 32.0, 380.0, 2240.0, double.NaN, "Drop")]
        public void ValidateRejectsOutOfRangeValues(double spacing, double snap, double inner, double outer, double drop, string name) {
            var settings = new FarGroundSettings { Spacing = spacing, Snap = snap, InnerRadius = inner, OuterRadius = outer, Drop = drop };

            var error = Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
            Assert.Equal(name, error.ParamName);
        }

        [Fact]
        public void SnapMayBeAnyWholeMultipleOfTheSpacing() {
            new FarGroundSettings { Spacing = 16.0, Snap = 16.0 }.Validate();
            new FarGroundSettings { Spacing = 24.0, Snap = 72.0 }.Validate();
        }

        [Fact]
        public void CloneIsIndependent() {
            var settings = new FarGroundSettings { Spacing = 8.0, Snap = 24.0, InnerRadius = 300.0, OuterRadius = 2400.0, Drop = 0.5 };
            FarGroundSettings copy = settings.Clone();
            settings.OuterRadius = 1.0;

            Assert.Equal(8.0, copy.Spacing);
            Assert.Equal(24.0, copy.Snap);
            Assert.Equal(300.0, copy.InnerRadius);
            Assert.Equal(2400.0, copy.OuterRadius);
            Assert.Equal(0.5, copy.Drop);
        }
    }
}
