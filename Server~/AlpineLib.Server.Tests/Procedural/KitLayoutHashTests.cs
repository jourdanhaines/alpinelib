using System;
using System.Numerics;
using AlpineLib.Procedural.Kits;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class KitLayoutHashTests {
        [Fact]
        public void EqualLayoutsHashEqual() {
            Assert.Equal(Build(false).ComputeHash(), Build(false).ComputeHash());
        }

        [Fact]
        public void HashIsOrderSensitive() {
            Assert.NotEqual(Build(false).ComputeHash(), Build(true).ComputeHash());
        }

        [Fact]
        public void HashIsPinnedAcrossRuns() {
            // Any drift in the hash stream (field order, prefixes, encoding) must fail loudly.
            Assert.Equal(PinnedHash, Build(false).ComputeHash());
        }

        [Fact]
        public void EveryFieldContributes() {
            ulong baseline = Single("Wall", "Wall@1,2", "Building", new KitPose(new Vector3(1, 2, 3), 90)).ComputeHash();

            Assert.NotEqual(baseline, Single("Walls", "Wall@1,2", "Building", new KitPose(new Vector3(1, 2, 3), 90)).ComputeHash());
            Assert.NotEqual(baseline, Single("Wall", "Wall@1,3", "Building", new KitPose(new Vector3(1, 2, 3), 90)).ComputeHash());
            Assert.NotEqual(baseline, Single("Wall", "Wall@1,2", "Roof", new KitPose(new Vector3(1, 2, 3), 90)).ComputeHash());
            Assert.NotEqual(baseline, Single("Wall", "Wall@1,2", "Building", new KitPose(new Vector3(1, 2, 3.0001f), 90)).ComputeHash());
            Assert.NotEqual(baseline, Single("Wall", "Wall@1,2", "Building", new KitPose(new Vector3(1, 2, 3), -90)).ComputeHash());
        }

        [Fact]
        public void NameBoundariesMatter() {
            ulong first = Single("ab", "c", "", KitPose.Identity).ComputeHash();
            ulong second = Single("a", "bc", "", KitPose.Identity).ComputeHash();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void NegativeZeroHashesAsZero() {
            ulong positive = Single("P", "", "", new KitPose(new Vector3(0f, 1f, 0f), 0f)).ComputeHash();
            ulong negative = Single("P", "", "", new KitPose(new Vector3(-0f, 1f, 0f), -0f)).ComputeHash();

            Assert.Equal(positive, negative);
        }

        [Fact]
        public void ClearEmptiesLayout() {
            KitLayout layout = Build(false);
            layout.Clear();

            Assert.Equal(0, layout.Count);
            Assert.Equal(new KitLayout().ComputeHash(), layout.ComputeHash());
        }

        [Fact]
        public void PlacementRequiresPartName() {
            Assert.Throws<ArgumentException>(() => new KitPlacement("", "x", "g", KitPose.Identity));
        }

        [Fact]
        public void QuarterTurnRotationIsExact() {
            var pose = new KitPose(new Vector3(3f, 1f, 2f), 10f);

            KitPose turned = pose.Rotated(90f);

            Assert.Equal(new Vector3(-1f, 3f, 2f), turned.Position);
            Assert.Equal(100f, turned.YawDegrees);
            Assert.Equal(new Vector3(1f, -3f, 2f), pose.Rotated(-90f).Position);
            Assert.Equal(new Vector3(-3f, -1f, 2f), pose.Rotated(540f).Position);
        }

        [Fact]
        public void TranslatedKeepsYaw() {
            KitPose moved = new KitPose(new Vector3(1f, 0f, 0f), 45f).Translated(new Vector3(0f, 2f, 1f));

            Assert.Equal(new Vector3(1f, 2f, 1f), moved.Position);
            Assert.Equal(45f, moved.YawDegrees);
        }

        private const ulong PinnedHash = 0x8995CCFF06B0EE26UL;

        private static KitLayout Build(bool swapped) {
            var layout = new KitLayout();
            var slab = new KitPlacement("PlatformSlab", "PlatformSlab@0,1.5", "Platform", new KitPose(new Vector3(0f, 1.5f, 0f), 0f));
            var wall = new KitPlacement("WallPanel", "WallPanel@5.9,4.5", "Building", new KitPose(new Vector3(5.9f, 4.5f, 1.5815f), -90f));
            layout.Add(swapped ? wall : slab);
            layout.Add(swapped ? slab : wall);
            return layout;
        }

        private static KitLayout Single(string part, string instance, string group, KitPose pose) {
            var layout = new KitLayout();
            layout.Add(part, instance, group, pose);
            return layout;
        }
    }
}
