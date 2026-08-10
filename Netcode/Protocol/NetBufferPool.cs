using System;
using System.Collections.Concurrent;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Recycles the fixed-size byte arrays that messages are serialized into. The send path runs at tick
    /// rate on every peer, so allocating a fresh buffer per message would hand the Unity GC a steady
    /// stream of garbage for no reason.
    ///
    /// Thread-safe by construction: chat and gameplay may serialize from different threads before their
    /// payloads are handed to the pumping thread, and a lock-free bag costs nothing here.
    /// </summary>
    public sealed class NetBufferPool {
        /// <summary>
        /// Default buffer size. Comfortably under the 1200-byte payload LiteNetLib can put in a single
        /// unfragmented datagram, with room for its own headers.
        /// </summary>
        public const int DefaultBufferSize = 1024;

        private readonly ConcurrentBag<byte[]> available = new ConcurrentBag<byte[]>();
        private readonly int bufferSize;
        private readonly int maxRetained;

        public NetBufferPool() : this(DefaultBufferSize, 64) { }

        public NetBufferPool(int bufferSize, int maxRetained) {
            if (bufferSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(bufferSize));
            }

            if (maxRetained <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maxRetained));
            }

            this.bufferSize = bufferSize;
            this.maxRetained = maxRetained;
        }

        /// <summary>Process-wide pool for callers with no reason to own one.</summary>
        public static NetBufferPool Shared { get; } = new NetBufferPool();

        /// <summary>Size of every buffer this pool hands out.</summary>
        public int BufferSize => bufferSize;

        /// <summary>Buffers currently sitting idle in the pool.</summary>
        public int AvailableCount => available.Count;

        /// <summary>Takes a buffer from the pool, allocating one only when the pool is empty.</summary>
        public byte[] Rent() {
            if (available.TryTake(out byte[] buffer)) {
                return buffer;
            }

            return new byte[bufferSize];
        }

        /// <summary>Convenience for the common case: rent a buffer and wrap it in a writer.</summary>
        public NetWriter RentWriter() {
            return new NetWriter(Rent());
        }

        /// <summary>
        /// Returns a buffer. Foreign-sized arrays and returns past the retention cap are dropped on the
        /// floor rather than corrupting the pool or letting it grow without bound after a traffic spike.
        /// </summary>
        public void Return(byte[] buffer) {
            if (buffer == null || buffer.Length != bufferSize) {
                return;
            }

            if (available.Count >= maxRetained) {
                return;
            }

            available.Add(buffer);
        }

        /// <summary>Drops every retained buffer. Used on shutdown so a pooled arena is not kept alive.</summary>
        public void Clear() {
            while (available.TryTake(out _)) {
            }
        }
    }
}
