using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // The draw cap holds what it draws: an earlier spawn coming into range while the cap is full waits for a free slot
    // instead of pushing a drawn prop out, and the waiting ones are admitted in spawn order once one fades.
    public sealed class RollerDrawCapTests {
        [Fact]
        public void ADrawnPropStaysDrawnWhenAnEarlierOneComesIntoRange() {
            var cap = new RollerDrawCap();
            var late = new List<RollerSpawn> { Spawn(10, 5.0), Spawn(11, 6.0), Spawn(12, 7.0) };
            cap.Select(late, 3);
            Assert.Equal(3, cap.Count);

            var withEarlier = new List<RollerSpawn> { Spawn(1, 1.0), Spawn(10, 5.0), Spawn(11, 6.0), Spawn(12, 7.0) };
            for (int frame = 0; frame < 5; frame++) {
                cap.Select(withEarlier, 3);
                Assert.Empty(cap.Dropped);
                Assert.Empty(cap.Admitted);
                Assert.False(cap.IsDrawn(1));
                foreach (ulong id in new ulong[] { 10, 11, 12 }) Assert.True(cap.IsDrawn(id));
            }

            var oneFaded = new List<RollerSpawn> { Spawn(1, 1.0), Spawn(2, 2.0), Spawn(10, 5.0), Spawn(12, 7.0) };
            cap.Select(oneFaded, 3);
            Assert.Equal(new ulong[] { 11 }, cap.Dropped);
            Assert.Single(cap.Admitted);
            Assert.Equal(1UL, oneFaded[cap.Admitted[0]].Id);
            Assert.True(cap.IsDrawn(1) && !cap.IsDrawn(2));
        }

        [Fact]
        public void TheSelectionIsInSpawnOrderAndNeverPastTheCap() {
            var cap = new RollerDrawCap();
            var candidates = new List<RollerSpawn>();
            for (int index = 0; index < 30; index++) candidates.Add(Spawn((ulong)(100 - index), 50.0 - index));

            cap.Select(candidates, 12);
            Assert.Equal(12, cap.Selected.Count);
            for (int index = 1; index < cap.Selected.Count; index++) Assert.True(candidates[cap.Selected[index - 1]].IsBefore(candidates[cap.Selected[index]]));
            Assert.Equal(candidates[29].Id, candidates[cap.Selected[0]].Id);
        }

        [Fact]
        public void SpentSpawnsOutliveAForgetAndArePrunedByDrift() {
            var spent = new RollerSpentSet();
            spent.Add(Spawn(7, 10.0), 500.0);
            spent.Add(Spawn(8, 20.0), 900.0);
            spent.Prune(499.0);
            Assert.True(spent.Contains(7) && spent.Contains(8));
            spent.Prune(500.0);
            Assert.False(spent.Contains(7));
            Assert.True(spent.Contains(8));
            spent.Clear();
            Assert.Equal(0, spent.Count);
        }

        private static RollerSpawn Spawn(ulong id, double drift) {
            return new RollerSpawn(id, 0, 0, 0, 0, drift, 1.0, 0.5, 300.0);
        }
    }
}
