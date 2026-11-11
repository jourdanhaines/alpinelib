using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// The rows a corner resolution holds: one dropped once the push clears its contact, and the shallowest left out
    /// when more shapes press on the pawn than there are rows.
    /// </summary>
    public sealed class PawnCornerConstraintsTests {
        [Fact]
        public void ARowIsDroppedOnceThePushClearsItsContact() {
            Span<Vector3> rows = stackalloc Vector3[PawnCornerConstraints.Capacity];
            Span<float> bounds = stackalloc float[PawnCornerConstraints.Capacity];
            Span<int> keys = stackalloc int[PawnCornerConstraints.Capacity];
            var constraints = new PawnCornerConstraints(rows, bounds, keys);
            CollisionContact[] contacts = { new CollisionContact(Vector3.UnitX, 0.05f, 1), new CollisionContact(Vector3.UnitZ, 0.05f, 2) };
            constraints.Merge(contacts, Vector3.Zero, false);
            Assert.Equal(2, constraints.Count);

            constraints.DropInactive(new Vector3(0.055f, 0f, 0.05f));

            Assert.Equal(1, constraints.Count);
            Assert.Equal(Vector3.UnitZ, constraints.Rows[0]);
        }

        [Fact]
        public void MoreShapesThanRowsLeaveOutTheShallowest() {
            Span<Vector3> rows = stackalloc Vector3[PawnCornerConstraints.Capacity];
            Span<float> bounds = stackalloc float[PawnCornerConstraints.Capacity];
            Span<int> keys = stackalloc int[PawnCornerConstraints.Capacity];
            var constraints = new PawnCornerConstraints(rows, bounds, keys);
            var contacts = new CollisionContact[PawnCornerConstraints.Capacity + 2];
            for (int index = 0; index < contacts.Length; index++) {
                float angle = index * 0.6f;
                float depth = 0.01f * (1 + (index * 7) % contacts.Length);
                contacts[index] = new CollisionContact(new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)), depth, index);
            }

            constraints.Merge(contacts, Vector3.Zero, false);

            Assert.Equal(8, constraints.Count);
            float shallowestHeld = float.MaxValue;
            for (int index = 0; index < constraints.Count; index++) shallowestHeld = MathF.Min(shallowestHeld, bounds[index]);
            Assert.True(shallowestHeld >= 0.03f - 1e-6f, $"held a {shallowestHeld:F3} m contact over a deeper one");
        }
    }
}
