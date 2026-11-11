using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// A pawn pushed into an acute concave corner made of two convex shapes ends every step outside both, comes to
    /// rest there without jitter, and is never thrown out faster than it walked in nor lifted by the walls.
    /// </summary>
    public sealed class PawnMotorCornerTests {
        private const float Tolerance = 0.01f;
        private const float RestSpread = 0.001f;
        private const int RestTicks = 60;
        private const float SpeedSlack = 1e-3f;

        public static IEnumerable<object[]> RecordedCases() {
            foreach (CornerCase corner in PawnMotorCornerCases.All()) yield return new object[] { corner.Name };
        }

        [Theory]
        [MemberData(nameof(RecordedCases))]
        public void RecordedGameCornersEndEveryStepOutside(string name) {
            CornerCase corner = Array.Find(PawnMotorCornerCases.All(), (candidate) => candidate.Name == name);
            CollisionWorld world = corner.World();
            MovementProfile profile = CornerGeometry.ShippedProfile();
            PawnState state = corner.Start;
            float worst = 0f;
            for (int index = 0; index < corner.Ticks; index++) {
                uint tick = corner.FirstTick + (uint)index;
                PawnInput input = corner.Input(tick);
                state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
                worst = MathF.Max(worst, CornerGeometry.Residual(world, in state, profile, tick));
            }

            Assert.True(worst <= Tolerance, $"{name}: left {worst:F4} m inside.");
        }

        [Fact]
        public void WedgesFromSixtyToOneSeventyFiveDegreesHoldThePawnOutside() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            var failures = new List<string>();
            float worst = 0f;
            for (int normalDegrees = 60; normalDegrees <= 175; normalDegrees += 5) {
                foreach (double wedgeYaw in new[] { 0.0, 37.0 }) {
                    worst = MathF.Max(worst, WalkWedge(normalDegrees, wedgeYaw, profile, failures));
                }
            }

            Assert.True(failures.Count == 0, $"worst {worst:F4} m; " + string.Join("; ", failures.GetRange(0, Math.Min(12, failures.Count))));
        }

        [Fact]
        public void BoxesLeaningOnAWallHoldThePawnOutside() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            var failures = new List<string>();
            float worst = 0f;
            foreach (double yaw in new[] { 8.0, 14.0, 20.0, 27.0 }) {
                foreach (double tilt in new[] { 0.0, 25.0, 45.0 }) {
                    CollisionShape[] shapes = CornerGeometry.LeaningBox(yaw, tilt);
                    foreach (double heading in new[] { -10.0, 0.0, 6.0, 15.0 }) {
                        Vector2 direction = CornerGeometry.Rotate(Vector2.UnitX, heading * Math.PI / 180.0);
                        var start = new Vector3(-6f, 0f, -0.6f);
                        CornerWalk walk = Walk(shapes, start, direction, WireLocomotion.Walk, false, 150, profile);
                        worst = MathF.Max(worst, walk.WorstResidual);
                        if (!walk.IsClean(Tolerance)) failures.Add($"yaw {yaw} tilt {tilt} heading {heading}: {walk}");
                    }
                }
            }

            Assert.True(failures.Count == 0, $"worst {worst:F4} m; " + string.Join("; ", failures.GetRange(0, Math.Min(12, failures.Count))));
        }

        [Fact]
        public void APawnPushedIntoANotchComesToRest() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            foreach (int normalDegrees in new[] { 120, 155, 160, 170 }) {
                CollisionShape[] shapes = CornerGeometry.Wedge(normalDegrees, new Vector2(3f, -2f), Vector2.Normalize(new Vector2(-1f, -0.3f)));
                Vector3 start = CornerGeometry.Mouth(new Vector2(3f, -2f), Vector2.Normalize(new Vector2(-1f, -0.3f)), CornerGeometry.RestDistance(normalDegrees, profile.CapsuleRadius) + 2f);
                Vector2 inward = -Vector2.Normalize(new Vector2(-1f, -0.3f));
                CornerWalk walk = Walk(shapes, start, CornerGeometry.Rotate(inward, 0.05), WireLocomotion.Sprint, false, 240, profile);

                Assert.True(walk.IsClean(Tolerance), $"{normalDegrees}°: {walk}");
                Assert.True(walk.RestSpread < RestSpread, $"{normalDegrees}°: moved {walk.RestSpread:F5} m over the last {RestTicks} ticks.");
            }
        }

        [Fact]
        public void AJumpIntoANotchLandsOutsideTheWalls() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            var failures = new List<string>();
            foreach (int normalDegrees in new[] { 130, 150, 160, 170 }) {
                Vector2 opening = Vector2.Normalize(new Vector2(0.4f, 1f));
                CollisionShape[] shapes = CornerGeometry.Wedge(normalDegrees, Vector2.Zero, opening);
                Vector3 start = CornerGeometry.Mouth(Vector2.Zero, opening, CornerGeometry.RestDistance(normalDegrees, profile.CapsuleRadius) + 1f);
                CornerWalk walk = Walk(shapes, start, -opening, WireLocomotion.Jog, true, 120, profile);
                if (!walk.IsClean(Tolerance)) failures.Add($"{normalDegrees}°: {walk}");
            }

            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }

        // Walks in from the mouth at headings across the wedge, at walking and sprinting speed.
        private static float WalkWedge(int normalDegrees, double wedgeYaw, MovementProfile profile, List<string> failures) {
            var apex = new Vector2(1.5f, -0.75f);
            Vector2 opening = CornerGeometry.Rotate(Vector2.UnitX, wedgeYaw * Math.PI / 180.0);
            CollisionShape[] shapes = CornerGeometry.Wedge(normalDegrees, apex, opening);
            float rest = CornerGeometry.RestDistance(normalDegrees, profile.CapsuleRadius);
            double halfInterior = (180.0 - normalDegrees) * 0.5 * Math.PI / 180.0;
            float worst = 0f;
            foreach (double share in new[] { -1.6, -0.9, -0.4, 0.0, 0.3, 0.8, 1.4 }) {
                double heading = Math.Clamp(share * halfInterior, -1.4, 1.4);
                foreach (WireLocomotion gait in new[] { WireLocomotion.Walk, WireLocomotion.Sprint }) {
                    Vector3 start = CornerGeometry.Mouth(apex, opening, rest + 2.5f);
                    int ticks = (int)((rest + 2.5f) / profile.GetSpeedForGait((int)gait) * 30f) + 90;
                    CornerWalk walk = Walk(shapes, start, CornerGeometry.Rotate(-opening, heading), gait, false, ticks, profile);
                    worst = MathF.Max(worst, walk.WorstResidual);
                    if (!walk.IsClean(Tolerance) || walk.GroundedRise > 0f) failures.Add($"{normalDegrees}° yaw {wedgeYaw} share {share} {gait}: {walk}");
                }
            }

            return worst;
        }

        private static CornerWalk Walk(CollisionShape[] shapes, Vector3 start, Vector2 direction, WireLocomotion gait, bool jump, int ticks, MovementProfile profile) {
            var world = new CollisionWorld(new SceneGeometry("Corner", 1u, shapes, Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
            var state = new PawnState(start, 0f, Vector3.Zero, PawnState.PackFlags(gait, false, true));
            var walk = new CornerWalk(profile.MaxSpeed * CornerGeometry.TickSeconds + SpeedSlack, RestTicks);
            for (int index = 0; index < ticks; index++) {
                uint tick = (uint)index + 1u;
                var input = new PawnInput(tick, direction, gait, jump && state.IsGrounded && index % 40 == 5, false);
                PawnState before = state;
                state = PawnMotor.Step(in input, in before, profile, world, tick, CornerGeometry.TickSeconds);
                walk.Observe(in before, in state, CornerGeometry.Residual(world, in state, profile, tick), index >= ticks - RestTicks);
            }

            return walk;
        }
    }
}
