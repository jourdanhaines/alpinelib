using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;
using Xunit.Abstractions;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Sloped prism aprons round a wall: straight pieces, a fan round a convex corner and a straight seam with a kink in
    /// slope direction, walked by the shipped profile at many headings. Every tick moves as commanded, with no sideways shove
    /// and no correction, and a walk at the wall ends against it.
    /// </summary>
    public sealed class CollisionSlopedPrismSeamTests {
        private const float TickSeconds = 1f / 30f;
        private const float Width = 10f;
        private const float Rise = 7f;
        private const float LateralLimit = 0.02f;

        private readonly ITestOutputHelper _output;

        public CollisionSlopedPrismSeamTests(ITestOutputHelper output) {
            _output = output;
        }

        [Fact]
        public void WalksOverTheFanAndItsSeamsMoveAsCommanded() {
            CollisionWorld world = World(FanApron(3));
            var problems = new List<string>();
            int walks = 0;
            for (int heading = 0; heading < 36; heading++) {
                double angle = heading * Math.PI / 18.0;
                var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                foreach (Vector2 start in new[] { new Vector2(4f, 4f), new Vector2(7f, 2f), new Vector2(2f, 7f), new Vector2(6f, -3f), new Vector2(-3f, 6f), new Vector2(3f, 1f) }) {
                    walks++;
                    string problem = WalkProblem(world, start, direction, 60);
                    if (problem != null) problems.Add($"start {start} heading {heading * 10}°: {problem}");
                }
            }
            foreach (string problem in problems.GetRange(0, Math.Min(12, problems.Count))) _output.WriteLine(problem);
            Assert.True(problems.Count == 0, problems.Count + " of " + walks + " walks: " + string.Join("; ", problems.GetRange(0, Math.Min(6, problems.Count))));
        }

        // Points on a slanted seam, rounded to floats, sit a hair outside one piece or the other or both; the support
        // still finds the apron under every one of them.
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        public void EveryPointOnAFanSeamIsSupported(int pieces) {
            CollisionShape[] shapes = FanApron(pieces);
            CollisionWorld world = World(shapes);
            int misses = 0, samples = 0;
            for (int seam = 1; seam < pieces; seam++) {
                double angle = seam * Math.PI / 2.0 / pieces;
                for (int step = 1; step < 400; step++) {
                    double along = Width * step / 400.0;
                    float x = (float)(along * Math.Cos(angle)), z = (float)(along * Math.Sin(angle));
                    samples++;
                    if (!world.TryGetSupport(x, z, Rise + 1f, -0.5f, 0u, out SupportHit hit) || world.GetStaticShape(hit.ShapeIndex).Type != CollisionShapeType.SlopedPrism) misses++;
                }
            }
            Assert.True(misses == 0, misses + " of " + samples + " seam points unsupported");
        }

        // A walk straight up a slanted fan seam: the foot column runs along the edge both pieces share, where an exact
        // point-in-plan test misses both about a fifth of the time.
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void WalksUpAlongEveryFanSeamMoveAsCommanded(int pieces) {
            CollisionWorld world = World(FanApron(pieces));
            var problems = new List<string>();
            for (int seam = 1; seam < pieces; seam++) {
                double angle = seam * Math.PI / 2.0 / pieces;
                foreach (float scale in new[] { 1f, 1.37f, 0.71f }) {
                    var start = new Vector2((float)((Width + 2f * scale) * Math.Cos(angle)), (float)((Width + 2f * scale) * Math.Sin(angle)));
                    Vector2 direction = Vector2.Normalize(-start);
                    string problem = WalkProblem(world, start, direction, 120);
                    if (problem != null) problems.Add($"seam {seam} from {start}: {problem}");
                }
            }
            foreach (string problem in problems) _output.WriteLine(problem);
            Assert.Empty(problems);
        }

        [Theory]
        [InlineData(0.25f)]
        [InlineData(-0.25f)]
        [InlineData(0.5f)]
        [InlineData(-0.5f)]
        public void WalksOverAKinkedStraightSeamMoveAsCommanded(float kink) {
            var plan = new[] { new Vector2(0f, -12f), new Vector2(Width, -12f), new Vector2(Width, 0f), new Vector2(0f, 0f) };
            var other = new[] { new Vector2(0f, 0f), new Vector2(Width, 0f), new Vector2(Width, 12f), new Vector2(0f, 12f) };
            CollisionShape[] shapes = {
                CollisionShape.MakePlane(0f),
                CollisionShape.MakeSlopedPrism(Vector3.Zero, -12f, Rise, -0.7f, 0f, plan),
                CollisionShape.MakeSlopedPrism(Vector3.Zero, -12f, Rise, -0.7f, kink, other),
                CollisionShape.MakePrism(new Vector3(-2f, 10f, 0f), 10f, new[] { new Vector2(-2f, -13f), new Vector2(2f, -13f), new Vector2(2f, 13f), new Vector2(-2f, 13f) }),
            };
            CollisionWorld world = World(shapes);
            var problems = new List<string>();
            for (int heading = 0; heading < 36; heading++) {
                double angle = heading * Math.PI / 18.0;
                var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                foreach (Vector2 start in new[] { new Vector2(5f, -1.5f), new Vector2(5f, 1.5f), new Vector2(2f, 0.3f), new Vector2(8f, -0.3f) }) {
                    string problem = WalkProblem(world, start, direction, 45);
                    if (problem != null) problems.Add($"start {start} heading {heading * 10}°: {problem}");
                }
            }
            foreach (string problem in problems.GetRange(0, Math.Min(12, problems.Count))) _output.WriteLine(problem);
            Assert.True(problems.Count == 0, problems.Count + " walks: " + string.Join("; ", problems.GetRange(0, Math.Min(6, problems.Count))));
        }

        [Fact]
        public void WalksStraightUpEveryPieceReachTheWall() {
            CollisionWorld world = World(FanApron(3));
            MovementProfile profile = ShippedProfile();
            var problems = new List<string>();
            // The straight aprons from the toe at the wall, then the fan from its toe arc at the corner.
            var walks = new List<(Vector2 Start, Vector2 Target)>();
            for (int index = 0; index < 5; index++) {
                float along = -10f + index * 2.25f;
                walks.Add((new Vector2(Width + 2f, along), new Vector2(0f, along)));
                walks.Add((new Vector2(along, Width + 2f), new Vector2(along, 0f)));
            }
            for (int index = 0; index <= 8; index++) {
                double angle = index * Math.PI / 16.0;
                walks.Add((new Vector2((float)((Width + 2f) * Math.Cos(angle)), (float)((Width + 2f) * Math.Sin(angle))), Vector2.Zero));
            }
            foreach ((Vector2 start, Vector2 target) in walks) {
                Vector2 direction = Vector2.Normalize(target - start);
                string problem = WalkProblem(world, start, direction, 150);
                PawnState last = WalkStates(world, start, direction, 150)[149];
                float gap = WallGap(last.Position) - profile.CapsuleRadius;
                if (problem != null) problems.Add($"from {start}: {problem}");
                if (gap > 0.3f) problems.Add($"from {start}: stopped {gap:0.00} m short at {last.Position}");
            }
            foreach (string problem in problems) _output.WriteLine(problem);
            Assert.Empty(problems);
        }

        // Plan distance to the wall block over x, z ≤ 0.
        private static float WallGap(Vector3 position) {
            float x = MathF.Max(position.X, 0f), z = MathF.Max(position.Z, 0f);
            return MathF.Sqrt(x * x + z * z);
        }

        // Steps the walk; returns the first tick whose planar move strays more than the limit from the commanded one, or
        // that the movement validator would correct.
        private string WalkProblem(CollisionWorld world, Vector2 start, Vector2 direction, int ticks) {
            MovementProfile profile = ShippedProfile();
            var validator = new MovementValidator(new NetConfig { MovementProfiles = new[] { profile } });
            var state = new PawnState(new Vector3(start.X, Ground(world, start), start.Y), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Jog, false, true));
            var still = new PawnInput(0, Vector2.Zero, WireLocomotion.Jog, false, false);
            state = PawnMotor.Step(in still, in state, profile, world, 0, TickSeconds);
            if (!state.IsGrounded) return "not grounded at start";
            for (uint tick = 1; tick <= ticks; tick++) {
                var input = new PawnInput(tick, direction, WireLocomotion.Jog, false, false);
                PawnState next = PawnMotor.Step(in input, in state, profile, world, tick, TickSeconds);
                if (validator.Validate(0, in state, in next, TickSeconds).RequiresCorrection) return $"correction at tick {tick} {state.Position} -> {next.Position}";
                var moved = new Vector2(next.Position.X - state.Position.X, next.Position.Z - state.Position.Z);
                float along = Vector2.Dot(moved, direction);
                float lateral = (moved - direction * along).Length();
                bool pushedBack = along > profile.JogSpeed * TickSeconds + LateralLimit;
                if (!next.IsGrounded) return state.IsGrounded && WallGap(next.Position) > profile.CapsuleRadius + 0.05f && OnApron(world, next) ? $"left the ground at tick {tick} {state.Position} -> {next.Position}" : null;
                if ((lateral > LateralLimit && !AgainstWall(world, next, profile)) || pushedBack) {
                    return $"tick {tick} moved {moved} (lateral {lateral:0.000}, along {along:0.000}) {state.Position} -> {next.Position} grounded {state.IsGrounded}->{next.IsGrounded}";
                }
                state = next;
                if (Math.Abs(state.Position.X) > 30f || Math.Abs(state.Position.Z) > 30f) break;
            }
            return null;
        }

        private static List<PawnState> WalkStates(CollisionWorld world, Vector2 start, Vector2 direction, int ticks) {
            MovementProfile profile = ShippedProfile();
            var state = new PawnState(new Vector3(start.X, Ground(world, start), start.Y), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Jog, false, true));
            var states = new List<PawnState>();
            for (uint tick = 1; tick <= ticks; tick++) {
                var input = new PawnInput(tick, direction, WireLocomotion.Jog, false, false);
                state = PawnMotor.Step(in input, in state, profile, world, tick, TickSeconds);
                states.Add(state);
            }
            return states;
        }

        // Over an apron piece, well inside its plan (walking off a piece's open side is a fall, not a seam).
        private static bool OnApron(CollisionWorld world, PawnState state) {
            return world.TryGetSupport(state.Position.X, state.Position.Z, state.Position.Y + 1f, state.Position.Y - 1f, 0u, out SupportHit hit)
                && world.GetStaticShape(hit.ShapeIndex).Type == CollisionShapeType.SlopedPrism && hit.Height > 0.05f;
        }

        // Sliding along the plumb wall (a flat prism within reach of a slightly fattened capsule) may turn the move.
        private static bool AgainstWall(CollisionWorld world, PawnState state, MovementProfile profile) {
            var reach = new CapsulePose(state.Position + new Vector3(0f, profile.StepOffset, 0f), profile.CapsuleRadius + 0.05f, profile.CapsuleHeight - profile.StepOffset);
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            int count = world.CollectContacts(in reach, 0u, contacts);
            for (int index = 0; index < count; index++) {
                if (world.GetStaticShape(contacts[index].ShapeIndex).Type == CollisionShapeType.Prism) return true;
            }
            return false;
        }

        private static float Ground(CollisionWorld world, Vector2 at) {
            return world.TryGetSupport(at.X, at.Y, 20f, -5f, 0u, out SupportHit hit) ? hit.Height : 0f;
        }

        // A wall block over x, z < 0 rising past the aprons; straight aprons on its +X and +Z faces and a fan of `pieces`
        // triangles round the corner, every piece's plane through the wall foot at Rise and the toe at 0.
        internal static CollisionShape[] FanApron(int pieces) {
            var shapes = new List<CollisionShape> {
                CollisionShape.MakePlane(0f),
                CollisionShape.MakePrism(new Vector3(-6f, 6f, -6f), 6f, new[] { new Vector2(-6f, -6f), new Vector2(6f, -6f), new Vector2(6f, 6f), new Vector2(-6f, 6f) }),
                CollisionShape.MakeSlopedPrism(Vector3.Zero, -1f, Rise, -Rise / Width, 0f, new[] { new Vector2(0f, -12f), new Vector2(Width, -12f), new Vector2(Width, 0f), new Vector2(0f, 0f) }),
                CollisionShape.MakeSlopedPrism(Vector3.Zero, -1f, Rise, 0f, -Rise / Width, new[] { new Vector2(-12f, 0f), new Vector2(0f, 0f), new Vector2(0f, Width), new Vector2(-12f, Width) }),
            };
            for (int piece = 0; piece < pieces; piece++) {
                double from = piece * Math.PI / 2.0 / pieces, to = (piece + 1) * Math.PI / 2.0 / pieces;
                double fromX = Math.Cos(from), fromZ = Math.Sin(from), toX = Math.Cos(to), toZ = Math.Sin(to);
                // slopeX·cos + slopeZ·sin = −Rise / Width at both toe points.
                double determinant = fromX * toZ - fromZ * toX;
                double target = -Rise / Width;
                double slopeX = target * (toZ - fromZ) / determinant;
                double slopeZ = target * (fromX - toX) / determinant;
                var plan = new[] { Vector2.Zero, new Vector2((float)(Width * fromX), (float)(Width * fromZ)), new Vector2((float)(Width * toX), (float)(Width * toZ)) };
                shapes.Add(CollisionShape.MakeSlopedPrism(Vector3.Zero, -1f, Rise, (float)slopeX, (float)slopeZ, plan));
            }
            return shapes.ToArray();
        }

        // The project's shipped player: 0.48 × 1.4 m capsule, 0.3 m step, 45°, jog 5.4 m/s.
        internal static MovementProfile ShippedProfile() {
            return new MovementProfile {
                WalkSlowSpeed = 2f, WalkSpeed = 4f, JogSpeed = 5.4f, SprintSpeed = 7f, CrouchSpeed = 1.6f, CrouchFastSpeed = 2.8f, Gravity = -20f, JumpVelocity = 6f,
                AirAcceleration = 16f, AirDrag = 0f, CapsuleRadius = 0.48f, CapsuleHeight = 1.4f, StepOffset = 0.3f, SlopeLimitDegrees = 45f,
            };
        }

        private static CollisionWorld World(CollisionShape[] shapes) {
            return new CollisionWorld(new SceneGeometry("Aprons", 1u, shapes, Array.Empty<MoverDefinition>()), TickSeconds);
        }
    }
}
