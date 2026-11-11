using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// Away from acute corners the motor's later resolutions never change a step that the one-contact motor ends clear:
    /// random walks over seeded scenes are stepped with and without them from the same state, and every step whose
    /// one-contact result ends within the 2 mm tolerance must match bit for bit.
    /// </summary>
    public sealed class PawnMotorCornerIdentityTests {
        private const int PawnsPerScene = 12;
        private const int TicksPerPawn = 300;
        private const float Tolerance = 0.002f;

        public static IEnumerable<object[]> Scenes() {
            for (int seed = 1; seed <= 10; seed++) yield return new object[] { "scatter", seed };
            yield return new object[] { "square", 0 };
            yield return new object[] { "square", 33 };
            yield return new object[] { "hexagon", 10 };
            yield return new object[] { "octagon", 5 };
        }

        [Theory]
        [MemberData(nameof(Scenes))]
        public void WalksWithoutAcuteCornersMatchTheOneContactMotorBitForBit(string kind, int seed) {
            CollisionShape[] shapes = kind switch {
                "scatter" => IdentityFields.Scatter(seed, 28),
                "square" => IdentityFields.Room(4, 4f, seed),
                "hexagon" => IdentityFields.Room(6, 4.5f, seed),
                _ => IdentityFields.Room(8, 5f, seed),
            };
            var world = new CollisionWorld(new SceneGeometry(kind, 1u, shapes, Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
            MovementProfile[] profiles = { CornerGeometry.ShippedProfile(), new MovementProfile { SlopeLimitDegrees = 45f, AirDrag = 0.5f }, new MovementProfile { CapsuleRadius = 0.3f, CapsuleHeight = 1f, StepOffset = 0.25f } };
            var random = new Random(seed * 7919 + kind.Length);
            int touching = 0;
            for (int pawn = 0; pawn < PawnsPerScene; pawn++) {
                touching += WalkBoth(world, profiles[pawn % profiles.Length], random, kind == "scatter" ? 12f : 3f);
            }

            Assert.True(touching > PawnsPerScene * TicksPerPawn / 20, $"Only {touching} steps ended touching anything.");
        }

        // Steps one random walk through both motors; returns how many steps ended against something.
        private static int WalkBoth(CollisionWorld world, MovementProfile profile, Random random, float spread) {
            Vector3 start = ClearStart(world, profile, random, spread);
            var joint = new PawnState(start, 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, false));
            PawnInput input = default;
            int touching = 0;
            for (int index = 0; index < TicksPerPawn; index++) {
                uint tick = (uint)index + 1u;
                if (index % 23 == 0 || random.Next(18) == 0) input = RandomInput(random, tick);
                input = new PawnInput(tick, input.MoveDirection, input.Gait, input.Jump && random.Next(6) == 0, input.Crouch);
                PawnState single = PawnMotor.Step(in input, in joint, profile, world, tick, CornerGeometry.TickSeconds, false);
                joint = PawnMotor.Step(in input, in joint, profile, world, tick, CornerGeometry.TickSeconds, true);
                if (CornerGeometry.Residual(world, in single, profile, tick) <= Tolerance) AssertSameBits(in single, in joint, tick);
                if (IsTouching(world, in joint, profile, tick)) touching++;
            }

            return touching;
        }

        // A spot on the floor whose widened capsule touches nothing but the floor.
        private static Vector3 ClearStart(CollisionWorld world, MovementProfile profile, Random random, float spread) {
            while (true) {
                var start = new Vector3((float)(random.NextDouble() * 2.0 - 1.0) * spread, 0f, (float)(random.NextDouble() * 2.0 - 1.0) * spread);
                if (!IsTouching(world, new PawnState(start, 0f, Vector3.Zero, 0), profile, 0u)) return start;
            }
        }

        private static PawnInput RandomInput(Random random, uint tick) {
            double angle = random.NextDouble() * Math.PI * 2.0;
            float length = random.Next(5) == 0 ? 0f : random.Next(4) == 0 ? 1.3f : (float)(0.3 + random.NextDouble() * 0.7);
            var direction = new Vector2((float)Math.Cos(angle) * length, (float)Math.Sin(angle) * length);
            return new PawnInput(tick, direction, (WireLocomotion)random.Next(4), random.Next(3) == 0, random.Next(8) == 0);
        }

        private static bool IsTouching(CollisionWorld world, in PawnState state, MovementProfile profile, uint tick) {
            var reach = new CapsulePose(state.Position - new Vector3(0f, 0.05f, 0f), profile.CapsuleRadius + 0.05f, profile.CapsuleHeight + 0.1f);
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            int count = world.CollectContacts(in reach, tick, contacts);
            for (int index = 0; index < count; index++) {
                if (contacts[index].Normal.Y < 0.99f) return true;
            }

            return false;
        }

        private static void AssertSameBits(in PawnState expected, in PawnState actual, uint tick) {
            bool same = Bits(expected.Position) == Bits(actual.Position) && Bits(expected.Velocity) == Bits(actual.Velocity) && expected.Flags == actual.Flags;
            Assert.True(same, $"tick {tick}: one-contact {expected.Position} {expected.Velocity}, joint {actual.Position} {actual.Velocity}");
        }

        private static (int, int, int) Bits(Vector3 value) {
            return (BitConverter.SingleToInt32Bits(value.X), BitConverter.SingleToInt32Bits(value.Y), BitConverter.SingleToInt32Bits(value.Z));
        }
    }
}
