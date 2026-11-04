using System;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class WindFieldTests {
        private static readonly WindSettings Settings = WindSettings.Default;

        [Fact]
        public void IncrementalAccumulationEqualsColdReplayBitForBit() {
            var incremental = new WindField(42UL, Settings);
            const double frame = 1.0 / 60.0;
            double time = 0.0;
            for (int index = 0; index < 60 * 3600; index++) {
                time += frame;
                incremental.Displacement(time);
            }

            long steps = (long)Math.Floor(time / WindField.StepSeconds);
            WindVector replayed = new WindField(42UL, Settings).ReplaySteps(steps);
            Assert.Equal(replayed, incremental.SumOfSteps(steps));
            Assert.Equal(new WindField(42UL, Settings).Displacement(time), incremental.Displacement(time));
        }

        [Fact]
        public void ColdJoinAtAnHourMatchesAnUncachedReplay() {
            var cold = new WindField(7UL, Settings);
            WindVector joined = cold.Displacement(3600.25);
            WindVector expected = cold.ReplaySteps(3600) + cold.Velocity(3600.5) * 0.25;

            Assert.Equal(expected, joined);
            Assert.Equal(3600L, cold.CachedSteps);
        }

        [Fact]
        public void RewindingThroughCheckpointsGivesTheSameBits() {
            var field = new WindField(3UL, Settings);
            WindVector early = field.Displacement(700.5);
            field.Displacement(2000.0);
            Assert.Equal(early, field.Displacement(700.5));
            Assert.Equal(field.ReplaySteps(1999), field.SumOfSteps(1999));
            Assert.Equal(field.ReplaySteps(5), field.SumOfSteps(5));
        }

        [Fact]
        public void DisplacementIsZeroAtAndBeforeTheStart() {
            var field = new WindField(1UL, Settings);
            Assert.Equal(WindVector.Zero, field.Displacement(0.0));
            Assert.Equal(WindVector.Zero, field.Displacement(-5.0));
            Assert.Equal(WindVector.Zero, field.Displacement(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => field.Displacement(double.PositiveInfinity));
        }

        [Fact]
        public void DisplacementIsContinuousAcrossSteps() {
            var field = new WindField(11UL, Settings);
            for (int step = 1; step < 200; step++) {
                WindVector before = field.Displacement(step - 1e-9);
                WindVector after = field.Displacement(step);
                Assert.True((after - before).Length < 1e-6, $"jump of {(after - before).Length} at step {step}");
            }
        }

        [Fact]
        public void DirectionIsUnitAndSpeedStaysInTheGustBand() {
            var field = new WindField(99UL, Settings);
            double min = double.MaxValue;
            double max = double.MinValue;
            for (double time = 0.0; time < 7200.0; time += 0.7) {
                Assert.InRange(field.Direction(time).Length, 1.0 - 1e-12, 1.0 + 1e-12);
                double speed = field.Speed(time);
                Assert.InRange(speed, Settings.MinSpeed, Settings.MaxSpeed);
                Assert.InRange(Math.Abs(field.Velocity(time).Length - speed), 0.0, 1e-9);
                min = Math.Min(min, speed);
                max = Math.Max(max, speed);
            }

            Assert.True(max - min > 0.3 * Settings.BaseSpeed, $"gusts barely move the speed ({min:0.00}..{max:0.00})");
        }

        [Fact]
        public void HeadingStaysWithinTheWanderAndActuallyWanders() {
            var field = new WindField(5UL, Settings);
            double limit = Settings.HeadingWanderDegrees * Math.PI / 180.0;
            double spread = 0.0;
            for (double time = 0.0; time < 7200.0; time += 5.0) {
                double offset = Math.IEEERemainder(field.Heading(time) - field.PrevailingHeading, 2.0 * Math.PI);
                Assert.InRange(offset, -limit - 1e-12, limit + 1e-12);
                spread = Math.Max(spread, Math.Abs(offset));
            }

            Assert.True(spread > limit * 0.2, $"heading never strays ({spread} rad)");
        }

        [Fact]
        public void SameSeedSameWindOtherSeedOtherWind() {
            var first = new WindField(1234UL, Settings);
            var second = new WindField(1234UL, Settings);
            var other = new WindField(1235UL, Settings);

            Assert.Equal(first.Displacement(321.75), second.Displacement(321.75));
            Assert.Equal(first.PrevailingHeading, second.PrevailingHeading);
            Assert.NotEqual(first.Displacement(321.75), other.Displacement(321.75));
            Assert.NotEqual(first.PrevailingHeading, other.PrevailingHeading);
        }

        [Fact]
        public void DriftFollowsThePrevailingHeading() {
            var field = new WindField(77UL, Settings);
            WindVector drift = field.Displacement(600.0);
            double heading = Math.Atan2(drift.X, drift.Z);
            double difference = Math.Abs(Math.IEEERemainder(heading - field.PrevailingHeading, 2.0 * Math.PI));

            Assert.True(drift.Length > 600.0 * Settings.MinSpeed * Math.Cos(Settings.HeadingWanderDegrees * Math.PI / 180.0) * 0.9);
            Assert.True(difference <= Settings.HeadingWanderDegrees * Math.PI / 180.0 + 1e-9);
        }

        [Fact]
        public void FullWanderBendsByExactlyTheWanderAngle() {
            var field = new WindField(21UL, new WindSettings(3.0, 35.0, 600.0, 0.4, 45.0, 8.0));
            double bent = Math.Atan(Math.Tan(35.0 * Math.PI / 180.0));
            Assert.Equal(35.0 * Math.PI / 180.0, bent, 12);
            Assert.InRange(field.Prevailing.Length, 1.0 - 1e-15, 1.0 + 1e-15);
        }

        [Fact]
        public void PinnedDisplacementGolden() {
            Assert.Empty(WindGoldenTable.FindMismatches());
        }

        [Fact]
        public void SettingsRejectBadValues() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WindSettings(-1.0, 35.0, 600.0, 0.4, 45.0, 8.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WindSettings(3.0, 35.0, 0.0, 0.4, 45.0, 8.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WindSettings(3.0, 35.0, 600.0, 1.0, 45.0, 8.0));
            Assert.Equal(1.8, WindSettings.Default.MinSpeed, 12);
            Assert.Equal(4.2, WindSettings.Default.MaxSpeed, 12);
        }
    }
}
