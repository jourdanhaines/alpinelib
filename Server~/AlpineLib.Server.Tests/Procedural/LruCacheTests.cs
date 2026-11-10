using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Procedural.Streaming;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class LruCacheTests {
        [Fact]
        public void BuildsOncePerKeyAndReturnsTheStoredValue() {
            var cache = new LruCache<ulong, string>(4);
            var builds = new int[1];

            string first = cache.GetOrAdd(7UL, () => Counted(builds, "seven"));
            string second = cache.GetOrAdd(7UL, () => Counted(builds, "other"));

            Assert.Equal("seven", first);
            Assert.Same(first, second);
            Assert.Equal(1, builds[0]);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void EvictsTheLeastRecentlyAskedWhenFull() {
            var cache = new LruCache<int, int>(3);
            cache.GetOrAdd(1, () => 1);
            cache.GetOrAdd(2, () => 2);
            cache.GetOrAdd(3, () => 3);
            cache.GetOrAdd(1, () => -1);

            cache.GetOrAdd(4, () => 4);

            Assert.Equal(3, cache.Count);
            Assert.False(cache.Contains(2));
            Assert.True(cache.Contains(1));
            Assert.True(cache.Contains(3));
            Assert.True(cache.Contains(4));

            cache.GetOrAdd(5, () => 5);
            Assert.False(cache.Contains(3));
            Assert.Equal(1, cache.GetOrAdd(1, () => -1));
        }

        [Fact]
        public void ContainsDoesNotRefreshAKey() {
            var cache = new LruCache<int, int>(2);
            cache.GetOrAdd(1, () => 1);
            cache.GetOrAdd(2, () => 2);

            Assert.True(cache.Contains(1));
            cache.GetOrAdd(3, () => 3);

            Assert.False(cache.Contains(1));
            Assert.True(cache.Contains(2));
        }

        [Fact]
        public void EvictedKeysRebuildOnTheNextAsk() {
            var cache = new LruCache<int, int>(1);
            int builds = 0;
            cache.GetOrAdd(1, () => ++builds);
            cache.GetOrAdd(2, () => 20);

            Assert.Equal(2, cache.GetOrAdd(1, () => ++builds));
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void ClearForgetsEveryEntry() {
            var cache = new LruCache<int, int>(4);
            cache.GetOrAdd(1, () => 1);
            cache.GetOrAdd(2, () => 2);

            cache.Clear();

            Assert.Equal(0, cache.Count);
            Assert.False(cache.Contains(1));
            Assert.Equal(10, cache.GetOrAdd(1, () => 10));
        }

        [Fact]
        public void UsesTheGivenKeyComparer() {
            var cache = new LruCache<string, int>(4, StringComparer.OrdinalIgnoreCase);
            cache.GetOrAdd("Mesa", () => 1);

            Assert.Equal(1, cache.GetOrAdd("MESA", () => 2));
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void RejectsBadArguments() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, int>(0));
            Assert.Throws<ArgumentNullException>(() => new LruCache<int, int>(1).GetOrAdd(1, null));
            Assert.Throws<ArgumentNullException>(() => new LruCache<string, int>(1).GetOrAdd(null, () => 1));
            Assert.Throws<ArgumentNullException>(() => new LruCache<string, int>(1).Contains(null));
        }

        [Fact]
        public void AFailedBuildIsForgottenAndRetriedOnTheNextAsk() {
            var cache = new LruCache<int, int>(2);
            var builds = new int[1];

            Assert.Throws<InvalidOperationException>(() => cache.GetOrAdd(1, () => Fail(builds)));
            Assert.Equal(0, cache.Count);
            Assert.Equal(7, cache.GetOrAdd(1, () => Counted(builds, 7)));
            Assert.Equal(2, builds[0]);
            Assert.Equal(7, cache.GetOrAdd(1, () => Counted(builds, 8)));
        }

        [Fact]
        public async Task AFailedBuildDoesNotForgetTheKeysNewerBuild() {
            var cache = new LruCache<int, int>(1);
            using var started = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            Task<int> first = Task.Run(() => cache.GetOrAdd(1, () => FailAfter(started, release)));
            started.Wait();

            cache.GetOrAdd(2, () => 2);
            Assert.Equal(10, cache.GetOrAdd(1, () => 10));
            release.Set();

            await Assert.ThrowsAsync<InvalidOperationException>(() => first);
            Assert.True(cache.Contains(1));
            Assert.Equal(10, cache.GetOrAdd(1, () => 11));
        }

        [Fact]
        public async Task AnEntryEvictedDuringItsBuildIsBuiltAgain() {
            var cache = new LruCache<int, int>(1);
            var builds = new int[1];
            using var started = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            Task<int> first = Task.Run(() => cache.GetOrAdd(1, () => Blocked(builds, started, release, 5)));
            started.Wait();

            cache.GetOrAdd(2, () => 2);
            Assert.Equal(6, cache.GetOrAdd(1, () => Counted(builds, 6)));
            release.Set();

            Assert.Equal(5, await first);
            Assert.Equal(2, builds[0]);
            Assert.Equal(6, cache.GetOrAdd(1, () => 7));
        }

        [Fact]
        public async Task ClearDuringABuildLetsItFinishAndForgetsIt() {
            var cache = new LruCache<int, int>(4);
            var builds = new int[1];
            using var started = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            Task<int> first = Task.Run(() => cache.GetOrAdd(1, () => Blocked(builds, started, release, 5)));
            started.Wait();

            cache.Clear();
            release.Set();

            Assert.Equal(5, await first);
            Assert.False(cache.Contains(1));
            Assert.Equal(9, cache.GetOrAdd(1, () => Counted(builds, 9)));
            Assert.Equal(2, builds[0]);
        }

        [Fact]
        public void ABuildMayAskForOtherKeys() {
            var cache = new LruCache<int, int>(4);

            int outer = cache.GetOrAdd(1, () => cache.GetOrAdd(2, () => 20) + 1);

            Assert.Equal(21, outer);
            Assert.Equal(20, cache.GetOrAdd(2, () => 0));
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public async Task ConcurrentAsksBuildEachKeyOnce() {
            const int keys = 8;
            const int threads = 32;
            const int asksPerThread = 200;
            var cache = new LruCache<int, int>(keys);
            var builds = new int[keys];
            using var start = new ManualResetEventSlim(false);
            var workers = new List<Task>();
            for (int thread = 0; thread < threads; thread++) {
                int offset = thread;
                workers.Add(Task.Factory.StartNew(() => AskRepeatedly(cache, builds, start, offset, keys, asksPerThread), TaskCreationOptions.LongRunning));
            }

            start.Set();
            await Task.WhenAll(workers);

            for (int key = 0; key < keys; key++) Assert.Equal(1, builds[key]);
            Assert.Equal(keys, cache.Count);
        }

        [Fact]
        public void ConcurrentAsksPastCapacityStayBounded() {
            var cache = new LruCache<int, int>(4);
            Parallel.For(0, 2000, (index) => Assert.Equal(index % 13 * 3, cache.GetOrAdd(index % 13, () => index % 13 * 3)));

            Assert.Equal(4, cache.Count);
        }

        private static void AskRepeatedly(LruCache<int, int> cache, int[] builds, ManualResetEventSlim start, int offset, int keys, int asks) {
            start.Wait();
            for (int ask = 0; ask < asks; ask++) {
                int key = (ask + offset) % keys;
                int value = cache.GetOrAdd(key, () => Build(builds, key));
                Assert.Equal(key * 10, value);
            }
        }

        private static int Counted(int[] builds, int value) {
            builds[0]++;
            return value;
        }

        private static int Fail(int[] builds) {
            builds[0]++;
            throw new InvalidOperationException("build failed");
        }

        private static int FailAfter(ManualResetEventSlim started, ManualResetEventSlim release) {
            started.Set();
            release.Wait();
            throw new InvalidOperationException("build failed");
        }

        private static int Blocked(int[] builds, ManualResetEventSlim started, ManualResetEventSlim release, int value) {
            Interlocked.Increment(ref builds[0]);
            started.Set();
            release.Wait();
            return value;
        }

        private static string Counted(int[] builds, string value) {
            builds[0]++;
            return value;
        }

        private static int Build(int[] builds, int key) {
            Interlocked.Increment(ref builds[key]);
            Thread.Sleep(5);
            return key * 10;
        }
    }
}
