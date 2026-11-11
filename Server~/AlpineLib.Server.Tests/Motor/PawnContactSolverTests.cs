using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// The joint push-out solver answers the shortest push satisfying every half-space, from one row up to three, says
    /// so when none exists, and the motor's corner path allocates nothing.
    /// </summary>
    public sealed class PawnContactSolverTests {
        private const float Precision = 1e-5f;

        [Fact]
        public void RowsAlreadySatisfiedNeedNoPush() {
            Assert.True(PawnContactSolver.TrySolve(new[] { Vector3.UnitX, -Vector3.UnitZ }, new[] { -0.5f, 0f }, out Vector3 push));

            Assert.Equal(Vector3.Zero, push);
        }

        [Fact]
        public void OneRowIsProjectedOntoAlongItsDirection() {
            Assert.True(PawnContactSolver.TrySolve(new[] { new Vector3(0f, 0f, 0.5f) }, new[] { 0.25f }, out Vector3 push));

            AssertNear(new Vector3(0f, 0f, 0.5f), push);
        }

        [Fact]
        public void TwoFacesOfANotchArePushedOutAlongTheirBisector() {
            Vector2 first = CornerGeometry.Rotate(Vector2.UnitX, 1.35);
            Vector2 second = CornerGeometry.Rotate(Vector2.UnitX, -1.35);
            var rows = new[] { new Vector3(first.X, 0f, first.Y), new Vector3(second.X, 0f, second.Y) };

            Assert.True(PawnContactSolver.TrySolve(rows, new[] { 0.1f, 0.1f }, out Vector3 push));

            AssertNear(new Vector3((float)(0.1 / Math.Cos(1.35)), 0f, 0f), push);
            Assert.True(PawnContactSolver.Dot(rows[0], push) >= 0.1f - Precision && PawnContactSolver.Dot(rows[1], push) >= 0.1f - Precision);
        }

        [Fact]
        public void AFarRowThatTheShortPushAlreadyClearsStaysInactive() {
            var rows = new[] { Vector3.UnitX, Vector3.Normalize(new Vector3(1f, 0f, 1f)) };

            Assert.True(PawnContactSolver.TrySolve(rows, new[] { 0.2f, 0.05f }, out Vector3 push));

            AssertNear(new Vector3(0.2f, 0f, 0f), push);
        }

        [Fact]
        public void ThreeFacesOfACornerMeetAtTheirVertex() {
            var rows = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

            Assert.True(PawnContactSolver.TrySolve(rows, new[] { 0.1f, 0.2f, 0.3f }, out Vector3 push));

            AssertNear(new Vector3(0.1f, 0.2f, 0.3f), push);
        }

        [Fact]
        public void OpposingFacesCloserThanTheCapsuleHaveNoPush() {
            Assert.False(PawnContactSolver.TrySolve(new[] { Vector3.UnitZ, -Vector3.UnitZ }, new[] { 0.1f, 0.1f }, out _));
        }

        [Fact]
        public void FacesAlmostOppositeAreAGapNotACorner() {
            float tilt = MathF.PI / 180f;
            var almostOpposite = new Vector3(-MathF.Cos(tilt), 0f, MathF.Sin(tilt));
            Assert.False(PawnContactSolver.TrySolve(new[] { Vector3.UnitX, almostOpposite }, new[] { 0.05f, 0.05f }, out _));

            float wide = 20f * MathF.PI / 180f;
            var opposing = new Vector3(-MathF.Cos(wide), 0f, MathF.Sin(wide));
            Assert.True(PawnContactSolver.TrySolve(new[] { Vector3.UnitX, opposing }, new[] { 0.05f, 0.05f }, out Vector3 push));
            Assert.True(push.Length() < 0.5f, $"pushed {push.Length():F3} m along the gap");
        }

        [Fact]
        public void PushingIntoACornerAllocatesNothing() {
            MovementProfile profile = CornerGeometry.ShippedProfile();
            var world = new CollisionWorld(new SceneGeometry("Corner", 1u, CornerGeometry.Wedge(160.0, Vector2.Zero, Vector2.UnitX), Array.Empty<MoverDefinition>()), CornerGeometry.TickSeconds);
            var state = new PawnState(CornerGeometry.Mouth(Vector2.Zero, Vector2.UnitX, CornerGeometry.RestDistance(160.0, profile.CapsuleRadius) + 0.3f), 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var input = new PawnInput(1u, -Vector2.UnitX, WireLocomotion.Sprint, false, false);
            state = PawnMotor.Step(in input, in state, profile, world, 1u, CornerGeometry.TickSeconds);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint tick = 2u; tick < 62u; tick++) state = PawnMotor.Step(in input, in state, profile, world, tick, CornerGeometry.TickSeconds);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);
            Assert.True(CornerGeometry.Residual(world, in state, profile, 61u) <= 0.01f);
        }

        private static void AssertNear(Vector3 expected, Vector3 actual) {
            Assert.True(Vector3.Distance(expected, actual) <= Precision, $"expected {expected}, got {actual}");
        }
    }
}
