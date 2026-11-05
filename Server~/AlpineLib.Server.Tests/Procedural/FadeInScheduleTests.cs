using System;
using AlpineLib.Procedural.Fading;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class FadeInScheduleTests {
        [Fact]
        public void HeldItemsReadZeroUntilStarted() {
            var schedule = new FadeInSchedule(1.0, new[] { 0f, 0.2f });

            Assert.True(schedule.IsHeld);
            Assert.Equal(0f, schedule.AmountOf(0, 100.0));
            Assert.False(schedule.IsDone(1e9));
        }

        [Fact]
        public void EachItemRampsOverTheDurationAfterItsDelay() {
            var schedule = new FadeInSchedule(1.0, new[] { 0f, 0.25f });
            schedule.StartAt(10.0);

            Assert.Equal(0f, schedule.AmountOf(0, 10.0));
            Assert.Equal(0.5f, schedule.AmountOf(0, 10.5), 5);
            Assert.Equal(0.25f, schedule.AmountOf(1, 10.5), 5);
            Assert.Equal(1f, schedule.AmountOf(0, 11.0));
            Assert.False(schedule.IsDone(11.2));
            Assert.True(schedule.IsDone(11.25));
            Assert.Equal(1f, schedule.AmountOf(1, 11.25));
        }

        [Fact]
        public void ANegativeShiftContinuesAnEarlierFade() {
            var schedule = new FadeInSchedule(1.0, new[] { 0.1f });
            schedule.StartAt(20.0);
            schedule.ShiftDelay(0, -0.5f);

            Assert.Equal(0.4f, schedule.AmountOf(0, 20.0), 5);
            Assert.Equal(0f, schedule.MaxDelay);
        }

        [Fact]
        public void ABulkShiftMovesOnlyTheFlaggedItems() {
            var schedule = new FadeInSchedule(1.0, new[] { 0.1f, 0.2f, 0.3f });
            schedule.StartAt(20.0);
            schedule.ShiftDelays(new[] { true, false, true }, -0.5f);

            Assert.Equal(-0.4f, schedule.DelayOf(0), 5);
            Assert.Equal(0.2f, schedule.DelayOf(1), 5);
            Assert.Equal(-0.2f, schedule.DelayOf(2), 5);
            Assert.Equal(0.2f, schedule.MaxDelay, 5);
            Assert.Throws<ArgumentException>(() => schedule.ShiftDelays(new[] { true }, 1f));
        }

        [Fact]
        public void OffsetsAreStableBoundedAndSpread() {
            float first = FadeInSchedule.OffsetFor(12.3456, -78.9, 0.3);
            Assert.Equal(first, FadeInSchedule.OffsetFor(12.3456, -78.9, 0.3));

            float lowest = float.MaxValue;
            float highest = float.MinValue;
            for (int index = 0; index < 500; index++) {
                float offset = FadeInSchedule.OffsetFor(index * 1.37, index * -0.71, 0.3);
                Assert.InRange(offset, 0f, 0.3f);
                lowest = Math.Min(lowest, offset);
                highest = Math.Max(highest, offset);
            }
            Assert.True(lowest < 0.03f && highest > 0.27f, $"offsets span {lowest}..{highest}");
            Assert.Equal(0f, FadeInSchedule.OffsetFor(1.0, 2.0, 0.0));
        }

        [Fact]
        public void RejectsABadDurationOrStart() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FadeInSchedule(0.0, null));
            var schedule = new FadeInSchedule(1.0, null);
            Assert.Throws<ArgumentOutOfRangeException>(() => schedule.StartAt(double.NaN));
        }
    }
}
