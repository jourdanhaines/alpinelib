using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // The drift clock inverts the wind's own drift (any thread, any order of questions), and a roll built over it heads
    // along the live wind at the moment it rolls each metre: centred on it within the wander, though the live wind
    // itself swings well past the wander off the prevailing direction.
    public sealed class RollerWindClockTests {
        private const ulong Seed = 0xC10CUL;

        [Fact]
        public void TimeAtInvertsTheDrift() {
            var wind = new WindField(Seed, WindSettings.Default);
            var clock = new RollerWindClock(new WindField(Seed, WindSettings.Default));
            int hint = 0;
            for (double time = 0.25; time < 9000.0; time += 13.7) {
                double drift = DriftAt(wind, time);
                Assert.Equal(time, clock.TimeAt(drift), 6);
                Assert.Equal(time, clock.TimeAt(drift, ref hint), 6);
            }
            Assert.Equal(0.0, clock.TimeAt(-5.0));
        }

        [Fact]
        public void ConcurrentQuestionsAgreeWithOneThread() {
            var wind = new WindField(Seed, WindSettings.Default);
            var shared = new RollerWindClock(wind);
            var alone = new RollerWindClock(new WindField(Seed, WindSettings.Default));
            var drifts = new List<double>();
            for (int index = 0; index < 4000; index++) drifts.Add((index * 7919 % 4000) * 6.1);

            var answers = new double[drifts.Count];
            Parallel.For(0, drifts.Count, (index) => answers[index] = shared.TimeAt(drifts[index]));
            for (int index = 0; index < drifts.Count; index++) Assert.Equal(alone.TimeAt(drifts[index]), answers[index]);
        }

        [Fact]
        public void RollsHeadAlongTheLiveWind() {
            var settings = RollerPathSettings.Default;
            var field = new WindField(Seed, WindSettings.Default);
            var clock = new RollerWindClock(field);
            double offPrevailing = 0.0, sum = 0.0;
            int samples = 0;
            for (int walk = 0; walk < 40; walk++) {
                ulong id = StableHash.Mix(41UL, (ulong)walk);
                double size = RollerPath.SizeOf(id, settings);
                double share = RollerPath.GroundSpeedShareOf(id, size, settings);
                double drift = 300.0 + walk * 157.0;
                var spawn = new RollerSpawn(id, 0, 0, 0, 0, drift, size, share, settings.MaxLife);
                RollerPath path = RollerPath.Build(spawn, 0.0, 0.0, 0.45 * size, spawn.Life, clock, settings, RollerTestWorld.Open(), null);
                Assert.NotNull(path);
                for (double distance = 0.0; distance <= path.Length; distance += 1.0) {
                    path.HeadingAt(distance, out double x, out double z);
                    WindVector live = field.Direction(clock.TimeAt(drift + distance / share));
                    double offLive = Degrees(x, z, live);
                    Assert.True(Math.Abs(offLive) <= settings.WanderDegrees + 0.5, $"walk {walk} at {distance} m: {offLive:0.0} deg off the live wind");
                    sum += offLive;
                    samples++;
                    offPrevailing = Math.Max(offPrevailing, Math.Abs(Degrees(x, z, field.Prevailing)));
                }
            }
            Assert.True(Math.Abs(sum / samples) < 4.0, $"headings centre {sum / samples:0.0} deg off the live wind");
            Assert.True(offPrevailing > settings.WanderDegrees + 10.0, $"headings never strayed past {offPrevailing:0.0} deg off the prevailing wind");
        }

        private static double Degrees(double x, double z, WindVector wind) {
            return Math.Atan2(x * wind.Z - z * wind.X, x * wind.X + z * wind.Z) * 180.0 / Math.PI;
        }

        private static double DriftAt(WindField wind, double time) {
            WindVector displacement = wind.Displacement(time);
            return displacement.X * wind.Prevailing.X + displacement.Z * wind.Prevailing.Z;
        }
    }
}
