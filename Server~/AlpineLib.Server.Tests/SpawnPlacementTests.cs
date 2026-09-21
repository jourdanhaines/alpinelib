using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Where the two shipped placements actually put people: apart from each other, and on the floor the
    /// scene really has rather than the plane the seats were authored on.
    /// </summary>
    /// <remarks>
    /// These run on the placements alone, with no session behind them, because a placement is a pure
    /// "next place please" and the interesting failures — two seats on the same spot, a spawn a metre
    /// inside the floor — are visible without one.
    /// </remarks>
    public sealed class SpawnPlacementTests {
        /// <summary>Height of the raised floor the probe fixtures put under the seats.</summary>
        private const float FloorHeight = 1.5f;

        private const uint AnyTick = 7u;

        [Fact]
        public void EveryRingSeatIsItsOwnPlace() {
            var placement = new RingSpawnPlacement(radiusMetres: 2f, seats: 8);
            var seen = new List<Vector3>();

            for (int seatIndex = 0; seatIndex < 8; seatIndex++) {
                seen.Add(NextPosition(placement));
            }

            Assert.Equal(8, CountDistinct(seen));

            foreach (Vector3 position in seen) {
                Assert.Equal(2f, MathF.Sqrt(position.X * position.X + position.Z * position.Z), 4);
            }
        }

        [Fact]
        public void TheRingStartsOverOnceItsSeatsAreFull() {
            var placement = new RingSpawnPlacement(radiusMetres: 2f, seats: 4);
            Vector3 first = NextPosition(placement);

            for (int seatIndex = 0; seatIndex < 3; seatIndex++) {
                NextPosition(placement);
            }

            Vector3 fifth = NextPosition(placement);

            Assert.Equal(first.X, fifth.X, 4);
            Assert.Equal(first.Z, fifth.Z, 4);
        }

        [Fact]
        public void ARingSeatSpawnsStandingAndGrounded() {
            var placement = new RingSpawnPlacement();

            PawnState state = placement.NextSpawnState(Member(), false, null, AnyTick);

            Assert.Equal(WireLocomotion.Walk, state.Locomotion);
            Assert.True(state.IsGrounded);
            Assert.False(state.IsCrouching);
            Assert.Equal(Vector3.Zero, state.Velocity);
            Assert.Equal(PawnState.WorldCarrierId, state.CarrierId);
        }

        [Fact]
        public void ARingWithNoSeatsIsRefused() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RingSpawnPlacement(seats: 0));
        }

        [Fact]
        public void TheRingDropsItsSeatsOntoTheFloorTheSceneHas() {
            var placement = new RingSpawnPlacement();
            CollisionWorld world = RaisedFloor();

            PawnState state = placement.NextSpawnState(Member(), false, world, AnyTick);

            Assert.Equal(FloorHeight, state.Position.Y, 4);
        }

        [Fact]
        public void ASeatOverNothingKeepsItsNominalHeight() {
            // The probe fixture's slab is four metres across, so a ring of fifty finds only open air.
            var placement = new RingSpawnPlacement(radiusMetres: 50f);

            PawnState state = placement.NextSpawnState(Member(), false, RaisedFloor(), AnyTick);

            Assert.Equal(0f, state.Position.Y, 4);
        }

        [Fact]
        public void APointAboveTheFallbackFloorKeepsItsAuthoredHeight() {
            // No geometry was exported, so the plane at zero is a guess and the marker is not.
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(new Vector3(10f, 2.5f, 0f), 0f) });

            PawnState state = placement.NextSpawnState(Member(), false, CollisionWorld.Flat(), AnyTick);

            Assert.Equal(2.5f, state.Position.Y, 4);
        }

        [Fact]
        public void AListIsHandedOutInOrderAndThenStartsOver() {
            var placement = new ListSpawnPlacement(new[] {
                new SpawnPoint(new Vector3(10f, 0f, 0f), 90f),
                new SpawnPoint(new Vector3(20f, 0f, 0f), 180f)
            });

            PawnState first = placement.NextSpawnState(Member(), false, null, AnyTick);
            PawnState second = placement.NextSpawnState(Member(), false, null, AnyTick);
            PawnState third = placement.NextSpawnState(Member(), false, null, AnyTick);

            Assert.Equal(10f, first.Position.X, 4);
            Assert.Equal(90f, first.YawDegrees, 4);
            Assert.Equal(20f, second.Position.X, 4);
            Assert.Equal(180f, second.YawDegrees, 4);

            // The third arrival is back on the first point, offset rather than standing inside the first.
            Assert.Equal(90f, third.YawDegrees, 4);
            Assert.NotEqual(first.Position, third.Position);
        }

        [Fact]
        public void OverflowArrivalsRingTheirPointWithoutOverlapping() {
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(new Vector3(10f, 0f, -4f)) });
            var seen = new List<Vector3>();

            for (int arrival = 0; arrival < 5; arrival++) {
                seen.Add(NextPosition(placement));
            }

            Assert.Equal(5, CountDistinct(seen));
            Assert.Equal(new Vector3(10f, 0f, -4f), seen[0]);

            for (int arrival = 1; arrival < seen.Count; arrival++) {
                Vector3 offset = seen[arrival] - seen[0];
                Assert.Equal(
                    ListSpawnPlacement.OverflowRadiusMetres,
                    MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z),
                    4);
            }
        }

        [Fact]
        public void AListPointIsProbedFromItsOwnAuthoredHeight() {
            // Authored a metre above the slab: the probe reaches down to it rather than taking the
            // authored height at face value.
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(new Vector3(0f, FloorHeight + 1f, 0f)) });

            PawnState state = placement.NextSpawnState(Member(), false, RaisedFloor(), AnyTick);

            Assert.Equal(FloorHeight, state.Position.Y, 4);
        }

        [Fact]
        public void AnEmptyListIsRefused() {
            Assert.Throws<ArgumentException>(() => new ListSpawnPlacement(Array.Empty<SpawnPoint>()));
            Assert.Throws<ArgumentNullException>(() => new ListSpawnPlacement(null));
        }

        [Fact]
        public void ANullWorldMeansTheAuthoredHeightStands() {
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(new Vector3(0f, 3f, 0f)) });

            PawnState state = placement.NextSpawnState(Member(), false, null, AnyTick);

            Assert.Equal(3f, state.Position.Y, 4);
        }

        /// <summary>
        /// A world whose only shape is a four-metre slab whose top sits at <see cref="FloorHeight"/> —
        /// finite on purpose, so a seat authored outside it finds no support at all.
        /// </summary>
        private static CollisionWorld RaisedFloor() {
            CollisionShape slab = CollisionShape.MakeBox(
                new Vector3(0f, FloorHeight - 0.5f, 0f),
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                new Vector3(4f, 0.5f, 4f));

            var geometry = new SceneGeometry("SpawnProbe", 0u, new[] { slab }, Array.Empty<MoverDefinition>());
            return new CollisionWorld(geometry, CollisionWorld.DefaultTickIntervalSeconds);
        }

        private static Vector3 NextPosition(ISpawnPlacement placement) {
            return placement.NextSpawnState(Member(), false, null, AnyTick).Position;
        }

        private static SessionMember Member() {
            return new SessionMember(1, PlayerId.NewId(), "Tester", false, 0);
        }

        private static int CountDistinct(IReadOnlyList<Vector3> positions) {
            var distinct = new HashSet<Vector3>();

            for (int index = 0; index < positions.Count; index++) {
                distinct.Add(positions[index]);
            }

            return distinct.Count;
        }
    }
}
