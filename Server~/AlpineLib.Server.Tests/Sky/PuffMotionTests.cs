using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class PuffMotionTests {
        private const float CruiseSpeed = 20f;

        [Theory]
        [InlineData(60.0, 2.5f)]
        [InlineData(144.0, 1.04f)]
        public void SnapKeepsEveryPuffAndTheEstimate(double hertz, float snap) {
            double frame = 1.0 / hertz;
            var motion = new PuffMotion();
            var simulation = new PuffSimulation(new PuffSettings(), 3UL);
            Vector3 position = Vector3.Zero;
            for (int step = 0; step < (int)(4.0 * hertz); step++) position = Advance(motion, simulation, position, frame, 0f);

            int live = simulation.Count;
            Dictionary<ulong, Vector3> before = WorldPositions(simulation, position);
            float speed = motion.Velocity.Length();
            position = Advance(motion, simulation, position, frame, snap);

            Assert.Equal(CruiseSpeed, speed, 2);
            Assert.Equal(speed, motion.Velocity.Length(), 3);
            Assert.Equal(0, motion.Teleports);
            Assert.Equal(1, motion.Snaps);
            Assert.True(simulation.Count >= live - 1, $"{live} puffs before the snap, {simulation.Count} after");
            foreach (KeyValuePair<ulong, Vector3> puff in WorldPositions(simulation, position)) {
                if (!before.TryGetValue(puff.Key, out Vector3 was)) continue;

                Assert.True(Vector3.Distance(was, puff.Value) < 0.1f, $"puff {puff.Key} jumped {Vector3.Distance(was, puff.Value):0.000} m with the snap");
            }
        }

        [Fact]
        public void TeleportDropsThePuffs() {
            var motion = new PuffMotion();
            var simulation = new PuffSimulation(new PuffSettings(), 4UL);
            Vector3 position = Vector3.Zero;
            for (int step = 0; step < 120; step++) position = Advance(motion, simulation, position, 1.0 / 30.0, 0f);
            Assert.True(simulation.Count > 0);

            Assert.False(motion.Measure(position + new Vector3(0f, 0f, (float)PuffMotion.DefaultTeleportDistance), 1.0 / 30.0, out _));
            Assert.Equal(1, motion.Teleports);
            Assert.Equal(Vector3.Zero, motion.Velocity);
        }

        [Fact]
        public void EstimateFollowsMotionWithinTheLimits() {
            var motion = new PuffMotion();
            Vector3 position = Vector3.Zero;
            motion.Measure(position, 1.0 / 60.0, out _);
            for (int step = 0; step < 120; step++) {
                position += new Vector3(30f / 60f, 0f, 0f);
                motion.Measure(position, 1.0 / 60.0, out _);
            }
            Assert.Equal(30.0, motion.Velocity.X, 3);

            position += new Vector3(30f / 60f, 0f, 0f);
            motion.Shift(new Vector3(-256f, 0f, 0f));
            motion.Measure(position - new Vector3(256f, 0f, 0f), 1.0 / 60.0, out Vector3 residual);
            Assert.Equal(30.0, motion.Velocity.X, 3);
            Assert.True(residual.Length() < 1e-3f, $"a rebase left {residual}");

            motion.Reset();
            Assert.True(motion.Measure(new Vector3(500f, 0f, 0f), 1.0 / 60.0, out _));
            Assert.Equal(Vector3.Zero, motion.Velocity);
        }

        // One step along +X at the cruise speed (plus a sideways snap), the puffs carried by the air relative to it.
        private static Vector3 Advance(PuffMotion motion, PuffSimulation simulation, Vector3 position, double frame, float snap) {
            Vector3 next = position + new Vector3((float)(CruiseSpeed * frame), 0f, snap);
            if (!motion.Measure(next, frame, out Vector3 residual)) simulation.Clear();
            else simulation.Translate(-residual);

            simulation.Step(frame, new Vector3(2f, 0f, 1f) - motion.Velocity, 8.0, 1, 0.0);
            return next;
        }

        private static Dictionary<ulong, Vector3> WorldPositions(PuffSimulation simulation, Vector3 emitter) {
            var positions = new Dictionary<ulong, Vector3>();
            for (int index = 0; index < simulation.Count; index++) positions[simulation[index].Index] = emitter + simulation[index].Offset;
            return positions;
        }
    }
}
