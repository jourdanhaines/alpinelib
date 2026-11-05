using System;
using AlpineLib.Procedural.Streaming;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class RetryBackoffTests {
        [Fact]
        public void DoublesFromTheFirstDelayUpToTheLongest() {
            var backoff = new RetryBackoff(1.0, 30.0);

            Assert.Equal(0.0, backoff.DelayAfter(0));
            Assert.Equal(1.0, backoff.DelayAfter(1));
            Assert.Equal(2.0, backoff.DelayAfter(2));
            Assert.Equal(16.0, backoff.DelayAfter(5));
            Assert.Equal(30.0, backoff.DelayAfter(6));
            Assert.Equal(30.0, backoff.DelayAfter(1000));
        }

        [Fact]
        public void RejectsBadDelays() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryBackoff(0.0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryBackoff(2.0, 1.0));
        }
    }
}
