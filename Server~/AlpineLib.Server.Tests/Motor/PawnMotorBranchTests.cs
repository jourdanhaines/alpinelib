using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// The fallbacks around the joint corner resolution and the ground clamp, each in a scene that reaches it: a crush
    /// keeps the one-contact push, a fall onto a slot too narrow stays on its rims, the push-out after a step up is
    /// taken only when it clears without changing height, a hold over a moving platform keeps the ride, a walk-off
    /// lift never runs on a jump.
    /// </summary>
    public sealed class PawnMotorBranchTests {
        private const float Tolerance = 0.0021f;

        [Fact]
        public void APlatformCrushingAPawnAgainstAWallKeepsTheOneContactPush() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            CollisionShape pusher = CollisionShape.MakeBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(1.5f, 1f, 0.5f));
            var sweep = new MoverPath(new[] { new Vector3(0f, 1f, -3f), new Vector3(0f, 1f, 1.2f) }, 2f, MoverLoopMode.PingPong, 0u);
            CollisionShape[] statics = { CollisionShape.MakePlane(0f), Box(-3f, 3f, 0f, 3f, 2f, 3f) };
            var world = new CollisionWorld(new SceneGeometry("crush", 1u, statics, new[] { new MoverDefinition(1, 1, in pusher, sweep) }), CornerGeometry.TickSeconds);
            PawnState state = Standing(0f, 0f, 0f);
            int crushed = 0;
            for (uint tick = 1u; tick <= 70u; tick++) {
                var input = new PawnInput(tick, Vector2.Zero, WireLocomotion.Walk, false, false);
                PawnState single = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds, false);
                PawnState after = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
                Assert.True(after.Position == single.Position, $"tick {tick}: {after.Position} instead of the one-contact {single.Position}");
                if (CornerGeometry.Residual(world, in after, profile, tick) > 0.05f) crushed++;
                state = after;
            }

            Assert.True(crushed > 5, $"the platform never crushed the pawn ({crushed} ticks)");
        }

        [Fact]
        public void APawnFallingOntoASlotItDoesNotFitStaysOnTheRims() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            const float width = 0.85f;
            CollisionShape[] shapes = { CollisionShape.MakePlane(0f), Box(-3f, -width / 2f, 0f, 2f, -3f, 3f), Box(width / 2f, 3f, 0f, 2f, -3f, 3f) };
            var world = new CollisionWorld(new SceneGeometry("slot", 1u, shapes, Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
            var state = new PawnState(new Vector3(0.02f, 2.6f, 0f), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, false));
            float lowest = state.Position.Y;
            for (uint tick = 1u; tick <= 60u; tick++) {
                var input = new PawnInput(tick, Vector2.Zero, WireLocomotion.Walk, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
                float residual = CornerGeometry.Residual(world, in state, profile, tick);
                Assert.True(residual <= Tolerance, $"tick {tick}: {residual:F4} m inside the slot's walls at {state.Position}");
                lowest = MathF.Min(lowest, state.Position.Y);
            }

            Assert.True(lowest > 1.6f, $"sank into the slot to {lowest:F3}");
            Assert.False(state.IsGrounded);
        }

        [Fact]
        public void AStepUpUnderALowCeilingKeepsItsFeetOnTheLedge() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            CollisionShape[] shapes = { CollisionShape.MakePlane(0f), Box(0f, 4f, 0f, 0.2f, -2f, 2f), Box(0.3f, 4f, 1.59f, 3f, -2f, 2f) };
            var world = new CollisionWorld(new SceneGeometry("ceiling", 1u, shapes, Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
            PawnState state = Standing(-1.5f, 0f, 0f);
            int onLedge = 0;
            for (uint tick = 1u; tick <= 40u; tick++) {
                var input = new PawnInput(tick, Vector2.UnitX, WireLocomotion.Walk, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
                Assert.True(state.IsGrounded, $"tick {tick}: lost the ground at {state.Position}");
                if (state.Position.X <= 0.1f) continue;

                onLedge++;
                Assert.True(MathF.Abs(state.Position.Y - 0.2f) < 1e-4f, $"tick {tick}: feet at {state.Position.Y} instead of on the ledge");
            }

            Assert.True(onLedge > 10, $"only {onLedge} ticks on the ledge");
        }

        [Fact]
        public void AStepUpUnderAnOverhangEndsOutsideIt() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            float lean = 20f * MathF.PI / 180f;
            var right = new Vector3(MathF.Cos(lean), MathF.Sin(lean), 0f);
            var up = new Vector3(-MathF.Sin(lean), MathF.Cos(lean), 0f);
            CollisionShape overhang = CollisionShape.MakeBox(new Vector3(0.8f, 2f, 0f), right, up, Vector3.UnitZ, new Vector3(0.3f, 2f, 3f));
            CollisionShape[] shapes = { CollisionShape.MakePlane(0f), Box(0f, 4f, 0f, 0.25f, -2f, 2f), overhang };
            var world = new CollisionWorld(new SceneGeometry("overhang", 1u, shapes, Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
            PawnState state = Standing(-1.5f, 0f, 0f);
            for (uint tick = 1u; tick <= 40u; tick++) {
                var input = new PawnInput(tick, Vector2.UnitX, WireLocomotion.Walk, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
                float residual = CornerGeometry.Residual(world, in state, profile, tick);
                Assert.True(residual <= Tolerance, $"tick {tick}: {residual:F4} m inside the overhang at {state.Position}");
            }

            Assert.True(state.IsGrounded && MathF.Abs(state.Position.Y - 0.25f) < 1e-4f, $"not standing on the ledge: {state.Position}");
        }

        [Fact]
        public void APawnHeldAtAStepDownOnAMovingPlatformKeepsRidingIt() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            CollisionShape floor = CollisionShape.MakeBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(4f, 0.1f, 4f));
            CollisionShape step = CollisionShape.MakeBox(new Vector3(-1.5f, 0.225f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(1.5f, 0.125f, 1f));
            CollisionShape block = CollisionShape.MakeBox(new Vector3(1f, 0.35f, 0.75f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(1f, 0.25f, 0.25f));
            var path = new MoverPath(new[] { Vector3.Zero, new Vector3(0f, 0f, 40f) }, 1.5f, MoverLoopMode.PingPong, 0u);
            var movers = new[] { new MoverDefinition(1, 1, in floor, path), new MoverDefinition(2, 1, in step, path), new MoverDefinition(3, 1, in block, path) };
            var world = new CollisionWorld(new SceneGeometry("ride", 1u, new[] { CollisionShape.MakePlane(-5f) }, movers), CornerGeometry.TickSeconds);
            PawnState state = Standing(-0.6f, 0.35f, 0.2f);
            float startHeight = state.Position.Y;
            for (uint tick = 1u; tick <= 30u; tick++) {
                var input = new PawnInput(tick, Vector2.UnitX, WireLocomotion.WalkSlow, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
                Assert.True(state.IsGrounded, $"tick {tick}: fell off the platform at {state.Position}");
                float probeTop = state.Position.Y + PawnMotor.GroundTolerance;
                float probeBottom = state.Position.Y - PawnMotor.GroundTolerance;
                bool rides = world.TryGetSupport(state.Position.X, state.Position.Z, probeTop, probeBottom, tick, out SupportHit hit) && hit.IsMover;
                Assert.True(rides, $"tick {tick}: grounded at {state.Position.Y:F4}, out of the ride probe's reach of the platform");
            }

            Assert.True(state.Position.Y < startHeight - 0.2f, $"never stepped down: {state.Position}");
        }

        [Fact]
        public void AStandingPawnWhoseMoveIsTakenBackStaysOnTheGroundItStoodOn() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            var world = new CollisionWorld(new SceneGeometry("talus run", 1u, TalusRun(0.3f), Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
            var start = new Vector3(2.0451488f, 0.73852265f, 2.5034792f);
            var state = new PawnState(start, 0f, new Vector3(3.628419f, 0f, 4.341116f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var into = new Vector2(0.97014254f, 0.24253564f);
            for (uint tick = 24u; tick <= 54u; tick++) {
                var input = new PawnInput(tick, into, (WireLocomotion)(tick % 4), false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
                Assert.True(state.IsGrounded, $"tick {tick}: lost the ground at {state.Position}");
                Assert.True(state.Position == start, $"tick {tick}: moved to {state.Position} from {start}");
            }
        }

        [Theory]
        [InlineData(30)]
        [InlineData(60)]
        public void JumpingBesideAStepHeightBlockReachesTheOneContactMotorsApex(int rate) {
            float tickSeconds = 1f / rate;
            MovementProfile profile = CornerGeometry.ShippedProfile();
            foreach (float height in new[] { 0.1f, 0.2f, 0.29f }) {
                foreach (float gap in new[] { 0.1f, 0.2f, 0.3f, 0.4f }) {
                    CollisionShape[] shapes = { CollisionShape.MakePlane(0f), Box(0f, 1f, 0f, height, -1f, 1f) };
                    var world = new CollisionWorld(new SceneGeometry("jump", 1u, shapes, Array.Empty<MoverDefinition>()), tickSeconds);
                    foreach (Vector2 direction in new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY }) {
                        float single = ApexGain(world, profile, -gap, direction, tickSeconds, false);
                        Assert.Equal(single, ApexGain(world, profile, -gap, direction, tickSeconds, true));
                    }
                }
            }
        }

        private static float ApexGain(CollisionWorld world, MovementProfile profile, float startX, Vector2 direction, float tickSeconds, bool resolveCorners) {
            PawnState state = Standing(startX, 0f, 0f);
            float apex = 0f;
            int ticks = (int)(1.2f / tickSeconds);
            for (int index = 1; index <= ticks; index++) {
                var input = new PawnInput((uint)index, direction, WireLocomotion.Walk, index == 1, false);
                state = PawnMotor.Step(in input, in state, profile, world, (uint)index, tickSeconds, resolveCorners);
                if (index > 1 && state.IsGrounded) break;

                apex = MathF.Max(apex, state.Position.Y);
            }

            return apex;
        }

        // A talus rising at the slope beside a cliff, with a row of half-buried rocks along its foot.
        private static CollisionShape[] TalusRun(float slope) {
            var plan = new[] { new Vector2(0f, -3f), new Vector2(10f, -3f), new Vector2(10f, 3f), new Vector2(0f, 3f) };
            var shapes = new System.Collections.Generic.List<CollisionShape> {
                CollisionShape.MakePlane(0f), CollisionShape.MakeSlopedPrism(Vector3.Zero, -0.5f, 0.05f, slope, 0f, plan), Box(-2f, 12f, 0f, 12f, 3f, 5f),
            };
            var random = new Random(7);
            for (int rock = 0; rock < 12; rock++) {
                float x = 1f + rock * 0.8f;
                float z = 1.6f + (float)random.NextDouble() * 1.2f;
                shapes.Add(CollisionShape.MakeSphere(new Vector3(x, slope * x + 0.05f, z), 0.25f + (float)random.NextDouble() * 0.4f));
            }

            return shapes.ToArray();
        }

        private static PawnState Standing(float x, float y, float z) {
            return new PawnState(new Vector3(x, y, z), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, true));
        }

        private static CollisionShape Box(float minX, float maxX, float minY, float maxY, float minZ, float maxZ) {
            var center = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);
            var extents = new Vector3((maxX - minX) / 2f, (maxY - minY) / 2f, (maxZ - minZ) / 2f);
            return CollisionShape.MakeBox(center, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, extents);
        }
    }
}
