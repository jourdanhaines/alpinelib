using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// What the corner, gap and ground-clamp resolutions may and may not do to a pawn: never carry it further in a step
    /// than the one-contact motor or its top speed would, stop it still at a gap it does not fit and let it leave,
    /// keep it falling, land it in a ditch, and never leave it heading into a wall or faster than it came.
    /// </summary>
    public sealed class PawnMotorRecoveryTests {
        private const float StrideSlack = 0.01f;
        private const float Tolerance = 0.01f;

        public static IEnumerable<object[]> DenseScenes() {
            for (int seed = 1; seed <= 6; seed++) {
                yield return new object[] { seed, 30 };
                yield return new object[] { seed, 60 };
            }
        }

        [Theory]
        [MemberData(nameof(DenseScenes))]
        public void NoStepCarriesThePawnFurtherThanTheOneContactMotorOrItsTopSpeed(int seed, int rate) {
            float tickSeconds = 1f / rate;
            var world = new CollisionWorld(new SceneGeometry("dense", 1u, ClutterFields.Dense(seed), Array.Empty<MoverDefinition>()), tickSeconds);
            MovementProfile[] profiles = { CornerGeometry.ShippedProfile(), new MovementProfile { SlopeLimitDegrees = 45f, AirDrag = 0.5f }, new MovementProfile { CapsuleRadius = 0.3f, CapsuleHeight = 1f, StepOffset = 0.25f } };
            var failures = new List<string>();
            int joint = 0;
            for (int pawn = 0; pawn < 12; pawn++) {
                MovementProfile profile = profiles[pawn % 3];
                var random = new Random(pawn * 31 + seed);
                var state = new PawnState(new Vector3((float)(random.NextDouble() * 14 - 7), 3f, (float)(random.NextDouble() * 14 - 7)), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, false));
                for (int index = 1; index <= 300; index++) {
                    double angle = ((index / 40) * 1.7 + pawn * 0.9) % (2 * Math.PI);
                    var input = new PawnInput((uint)index, new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)), (WireLocomotion)random.Next(4), random.NextDouble() < 0.03, false);
                    PawnState single = PawnMotor.Step(in input, in state, profile, world, (uint)index, tickSeconds, false);
                    PawnState after = PawnMotor.Step(in input, in state, profile, world, (uint)index, tickSeconds);
                    if (after.Position != single.Position) joint++;
                    float limit = MathF.Max(Stride(in state, in single), (float)(profile.MaxSpeed * tickSeconds)) + StrideSlack;
                    if (Stride(in state, in after) > limit && failures.Count < 8) failures.Add($"pawn {pawn} tick {index}: {Stride(in state, in after):F4} > {limit:F4}");
                    state = after;
                }
            }

            Assert.True(failures.Count == 0, string.Join("; ", failures));
            Assert.True(joint > 0, "The joint resolutions never changed a step.");
        }

        [Fact]
        public void APawnSprintingAtAGapItDoesNotFitStopsStillAndCanLeave() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            float tickSeconds = 1f / 60f;
            var world = new CollisionWorld(new SceneGeometry("dense5", 1u, ClutterFields.Dense(5), Array.Empty<MoverDefinition>()), tickSeconds);
            var into = new Vector2(-0.6020119f, 0.7984871f);
            var state = new PawnState(new Vector3(3.2187486f, 0f, -1.2088305f), 0f, new Vector3(-0.5032793f, 0f, 1.7936012f), PawnState.PackFlags(WireLocomotion.Sprint, false, true));
            var held = new List<Vector3>();
            float worst = 0f;
            for (uint tick = 1u; tick <= 90u; tick++) {
                var input = new PawnInput(tick, into, (WireLocomotion)(1 + tick % 3), false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, tickSeconds);
                worst = MathF.Max(worst, CornerGeometry.Residual(world, in state, profile, tick));
                if (tick > 30u) held.Add(state.Position);
            }

            Assert.True(worst <= Tolerance, $"left {worst:F4} m inside");
            Assert.True(Spread(held) < 0.001f, $"moved {Spread(held):F5} m while holding into the gap");
            Assert.True(state.IsGrounded && state.Position.Y == 0f, $"sank or rose to {state.Position}");

            Vector3 stopped = state.Position;
            for (uint tick = 91u; tick <= 150u; tick++) {
                var input = new PawnInput(tick, -into, WireLocomotion.Walk, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, tickSeconds);
            }

            Assert.True(Vector3.Distance(stopped, state.Position) > 1f, $"walked back out only to {state.Position}");
        }

        [Fact]
        public void APawnJumpingAtAGapItDoesNotFitComesBackDown() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            float tickSeconds = 1f / 60f;
            var world = new CollisionWorld(new SceneGeometry("dense5", 1u, ClutterFields.Dense(5), Array.Empty<MoverDefinition>()), tickSeconds);
            var into = new Vector2(-0.6020119f, 0.7984871f);
            var state = new PawnState(new Vector3(3.2187486f, 0f, -1.2088305f), 0f, new Vector3(-0.5032793f, 0f, 1.7936012f), PawnState.PackFlags(WireLocomotion.Sprint, false, true));
            int airborne = 0;
            float highest = 0f;
            for (uint tick = 1u; tick <= 120u; tick++) {
                var input = new PawnInput(tick, into, WireLocomotion.Sprint, tick == 40u, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, tickSeconds);
                if (!state.IsGrounded) airborne++;
                highest = MathF.Max(highest, state.Position.Y);
            }

            Assert.True(highest > 0.5f, $"never left the ground (highest {highest:F3})");
            Assert.True(state.IsGrounded, $"still airborne at {state.Position} v{state.Velocity}");
            Assert.True(airborne < 60, $"airborne for {airborne} ticks");
        }

        [Fact]
        public void APawnFallingIntoADitchLandsNoLaterThanTheOneContactMotor() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            var failures = new List<string>();
            foreach (float slope in new[] { 0.4f, 0.7f, 1f }) {
                var world = new CollisionWorld(new SceneGeometry("ditch", 1u, ClutterFields.Ditch(slope), Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
                foreach (Vector2 spot in new[] { new Vector2(4.5f, 0.4f), new Vector2(5.4f, 0.4f), new Vector2(5.2f, 1.6f), new Vector2(5.5f, 2f), new Vector2(5.6f, 3.2f), new Vector2(5.3f, 2.6f) }) {
                    float x = spot.X;
                    var start = new PawnState(new Vector3(x, ClutterFields.DitchHeight(slope, x) + 1.2f, spot.Y), 0f, new Vector3(3f, 0f, 0f), PawnState.PackFlags(WireLocomotion.Sprint, false, false));
                    int joint = AirborneTicks(world, in start, profile, true, out float jointResidual);
                    int single = AirborneTicks(world, in start, profile, false, out float _);
                    if (joint > single || joint >= 60 || jointResidual > Tolerance) failures.Add($"slope {slope} at {spot}: airborne {joint} ticks (one-contact {single}), residual {jointResidual:F4}");
                }
            }

            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }

        [Fact]
        public void AJointCornerStepLeavesTheVelocityHeadingIntoNoWallAndNoFaster() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            var failures = new List<string>();
            int checkedSteps = 0;
            foreach (int normalDegrees in new[] { 100, 140, 160, 172 }) {
                Vector2 opening = Vector2.Normalize(new Vector2(-1f, 0.35f));
                var world = new CollisionWorld(new SceneGeometry("wedge", 1u, CornerGeometry.Wedge(normalDegrees, Vector2.Zero, opening), Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
                Vector3 mouth = CornerGeometry.Mouth(Vector2.Zero, opening, CornerGeometry.RestDistance(normalDegrees, profile.CapsuleRadius) + 1f);
                foreach (bool grounded in new[] { true, false }) {
                    var state = new PawnState(mouth + new Vector3(0f, grounded ? 0f : 1f, 0f), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Sprint, false, grounded));
                    bool lastWasJoint = false;
                    for (uint tick = 1u; tick <= 60u; tick++) {
                        var input = new PawnInput(tick, CornerGeometry.Rotate(-opening, 0.2), WireLocomotion.Sprint, false, false);
                        PawnState before = state;
                        PawnState single = PawnMotor.Step(in input, in before, profile, world, tick, CornerGeometry.TickSeconds, false);
                        state = PawnMotor.Step(in input, in before, profile, world, tick, CornerGeometry.TickSeconds);
                        lastWasJoint = state.Position != single.Position || state.Velocity != single.Velocity;
                        if (!lastWasJoint) continue;

                        checkedSteps++;
                        CheckVelocity(world, in before, in state, profile, tick, failures, $"{normalDegrees}° {(grounded ? "walking" : "falling")} tick {tick}");
                    }

                    if (lastWasJoint && state.IsGrounded && (state.Velocity.X != 0f || state.Velocity.Z != 0f)) failures.Add($"{normalDegrees}° wedged but still moving {state.Velocity}");
                }
            }

            Assert.True(failures.Count == 0, string.Join("; ", failures.GetRange(0, Math.Min(8, failures.Count))));
            Assert.True(checkedSteps > 20, $"Only {checkedSteps} steps went through the joint resolution.");
        }

        // Speed may not grow beyond the commanded gait or the fall, and no wall the capsule touches may be headed into.
        private static void CheckVelocity(CollisionWorld world, in PawnState before, in PawnState after, MovementProfile profile, uint tick, List<string> failures, string label) {
            float horizontal = MathF.Sqrt(after.Velocity.X * after.Velocity.X + after.Velocity.Z * after.Velocity.Z);
            if (horizontal > profile.MaxSpeed + 1e-3f) failures.Add($"{label}: horizontal speed {horizontal:F4}");
            if (after.Velocity.Y > MathF.Max(before.Velocity.Y, 0f) + 1e-3f) failures.Add($"{label}: rising at {after.Velocity.Y:F4}");

            var reach = new CapsulePose(after.Position + new Vector3(0f, after.IsGrounded ? profile.StepOffset : 0f, 0f), profile.CapsuleRadius + 0.01f, profile.CapsuleHeight - (after.IsGrounded ? profile.StepOffset : 0f));
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            int count = world.CollectContacts(in reach, tick, contacts);
            for (int index = 0; index < count; index++) {
                Vector3 normal = contacts[index].Normal;
                if (normal.Y > 0.7f) continue;

                float into = after.Velocity.X * normal.X + after.Velocity.Z * normal.Z;
                if (into < -1e-3f) failures.Add($"{label}: velocity {after.Velocity} heads into wall {normal} at {into:F4}");
            }
        }

        private static int AirborneTicks(CollisionWorld world, in PawnState start, MovementProfile profile, bool resolveCorners, out float worstResidual) {
            PawnState state = start;
            worstResidual = 0f;
            for (uint tick = 1u; tick <= 90u; tick++) {
                var input = new PawnInput(tick, Vector2.UnitX, WireLocomotion.Sprint, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds, resolveCorners);
                worstResidual = MathF.Max(worstResidual, CornerGeometry.Residual(world, in state, profile, tick));
                if (state.IsGrounded) return (int)tick;
            }

            return 90;
        }

        private static float Stride(in PawnState before, in PawnState after) {
            float x = after.Position.X - before.Position.X;
            float z = after.Position.Z - before.Position.Z;
            return MathF.Sqrt(x * x + z * z);
        }

        private static float Spread(List<Vector3> positions) {
            Vector3 minimum = positions[0];
            Vector3 maximum = positions[0];
            foreach (Vector3 position in positions) {
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);
            }

            Vector3 extent = maximum - minimum;
            return MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        }
    }
}
