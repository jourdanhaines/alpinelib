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
    /// The spawn layouts the overflow scheme has to survive, and the moving floor a probe answers for.
    /// </summary>
    /// <remarks>
    /// Kept separate from <c>SpawnPlacementTests</c> because each fact here names an authored layout the
    /// overflow ring has to keep arrivals apart on, rather than a property of the placement in isolation.
    /// </remarks>
    public sealed class SpawnPlacementReviewTests {
        private const uint AnyTick = 7u;

        /// <summary>
        /// Authored points exactly one overflow radius apart along +X — the spacing a hand-authored row of
        /// platform markers has, and the one an offset shared by every point would fold onto itself.
        /// Seeding the seat from the arrival ordinal and turning the ring half a seat off the axes keeps
        /// the overflow arrival clear of both authored points.
        /// </summary>
        [Fact]
        public void OverflowNeverLandsOnAnAlreadyOccupiedAuthoredPoint() {
            var placement = new ListSpawnPlacement(new[] {
                new SpawnPoint(new Vector3(0f, 0f, 0f)),
                new SpawnPoint(new Vector3(ListSpawnPlacement.OverflowRadiusMetres, 0f, 0f))
            });

            Vector3 first = NextPosition(placement);
            Vector3 second = NextPosition(placement);
            Vector3 third = NextPosition(placement);

            Assert.NotEqual(first, third);
            Assert.NotEqual(second, third);
        }

        /// <summary>
        /// The same point authored twice by mistake: consecutive arrivals still take different overflow
        /// seats, so the duplicate costs a doubled-up spot rather than two pawns inside each other.
        /// </summary>
        [Fact]
        public void TwoIdenticalPointsKeepTheirOverflowArrivalsApart() {
            var placement = new ListSpawnPlacement(new[] {
                new SpawnPoint(new Vector3(3f, 0f, 3f)),
                new SpawnPoint(new Vector3(3f, 0f, 3f))
            });

            var seen = new List<Vector3>();

            for (int arrival = 0; arrival < 4; arrival++) {
                seen.Add(NextPosition(placement));
            }

            // The first two share the authored point — that is what was authored — but the overflow pair
            // that follows must not land on either of them or on each other.
            Assert.Equal(seen[0], seen[1]);
            Assert.Equal(3, CountDistinct(seen));
            Assert.NotEqual(seen[2], seen[3]);
        }

        /// <summary>
        /// One authored point and a raised lobby cap. The overflow ring widens by a radius after a full
        /// revolution, so a lobby twice the size of one ring still gives every arrival a place of its own.
        /// </summary>
        [Fact]
        public void AListOfOnePlacesTenArrivalsInTenPlaces() {
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(Vector3.Zero) });
            var seen = new List<Vector3>();

            for (int arrival = 0; arrival < 10; arrival++) {
                seen.Add(NextPosition(placement));
            }

            Assert.Equal(10, CountDistinct(seen));
        }

        /// <summary>
        /// The first overflow revolution still sits at the authored radius — the widening only starts once
        /// a whole revolution has been handed out, so the common case stays beside its point.
        /// </summary>
        [Fact]
        public void TheFirstOverflowRevolutionKeepsTheAuthoredRadius() {
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(Vector3.Zero) });
            Vector3 point = NextPosition(placement);

            for (int seat = 0; seat < ListSpawnPlacement.OverflowSeats; seat++) {
                Vector3 offset = NextPosition(placement) - point;
                Assert.Equal(
                    ListSpawnPlacement.OverflowRadiusMetres,
                    MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z),
                    4);
            }

            Vector3 nextRevolution = NextPosition(placement) - point;
            Assert.Equal(
                2f * ListSpawnPlacement.OverflowRadiusMetres,
                MathF.Sqrt(nextRevolution.X * nextRevolution.X + nextRevolution.Z * nextRevolution.Z),
                4);
        }

        /// <summary>A platform under the seat is a floor: the probe asks the movers about the tick it was given.</summary>
        [Fact]
        public void ASeatOverAPlatformStandsOnThePlatform() {
            CollisionWorld world = PlatformWorld(out uint tickOverPlatform);
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(Vector3.Zero) });

            PawnState state = placement.NextSpawnState(Member(), false, world, tickOverPlatform);

            // The platform's top is at y = 1; the static floor it slides over is at y = 0.
            Assert.Equal(1f, state.Position.Y, 3);
        }

        /// <summary>The same seat, a tick at which the platform has slid away, falls back to the static floor.</summary>
        [Fact]
        public void ASeatThePlatformHasLeftStandsOnTheFloorBelow() {
            CollisionWorld world = PlatformWorld(out uint tickOverPlatform);
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(Vector3.Zero) });

            PawnState state = placement.NextSpawnState(Member(), false, world, tickOverPlatform + 150u);

            Assert.Equal(0f, state.Position.Y, 3);
        }

        /// <summary>
        /// Characterisation, not a defect: the probe reaches only two metres above the authored plane, so
        /// a marker authored more than that below the real floor keeps its nominal height and the pawn
        /// spawns inside the geometry. Pinned here because the type's remarks now promise it.
        /// </summary>
        [Fact]
        public void APointAuthoredWellBelowTheFloorIsLeftInsideIt() {
            CollisionWorld world = RaisedFloor(5f);
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(new Vector3(0f, -1f, 0f)) });

            PawnState state = placement.NextSpawnState(Member(), false, world, AnyTick);

            Assert.Equal(-1f, state.Position.Y, 3);
        }

        private static CollisionWorld RaisedFloor(float height) {
            CollisionShape slab = CollisionShape.MakeBox(
                new Vector3(0f, height - 0.5f, 0f),
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                new Vector3(20f, 0.5f, 20f));

            var geometry = new SceneGeometry("ReviewProbe", 0u, new[] { slab }, Array.Empty<MoverDefinition>());
            return new CollisionWorld(geometry, CollisionWorld.DefaultTickIntervalSeconds);
        }

        /// <summary>
        /// A ground slab at y = 0 with a platform whose top passes through the origin at tick zero and
        /// and has slid twenty metres away half a cycle later.
        /// </summary>
        private static CollisionWorld PlatformWorld(out uint tickOverPlatform) {
            CollisionShape ground = CollisionShape.MakeBox(
                new Vector3(0f, -0.5f, 0f),
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                new Vector3(40f, 0.5f, 40f));

            CollisionShape platform = CollisionShape.MakeBox(
                new Vector3(0f, 0.5f, 0f),
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                new Vector3(2f, 0.5f, 2f));

            var path = new MoverPath(
                new[] { Vector3.Zero, new Vector3(40f, 0f, 0f) },
                4f,
                MoverLoopMode.Loop,
                0u);

            var mover = new MoverDefinition(1, 0, in platform, path);
            var geometry = new SceneGeometry("ReviewPlatform", 0u, new[] { ground }, new[] { mover });

            tickOverPlatform = 0u;
            return new CollisionWorld(geometry, CollisionWorld.DefaultTickIntervalSeconds);
        }

        private static Vector3 NextPosition(ISpawnPlacement placement) {
            return placement.NextSpawnState(Member(), false, null, AnyTick).Position;
        }

        private static SessionMember Member() {
            return new SessionMember(1, PlayerId.NewId(), "Reviewer", false, 0);
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
