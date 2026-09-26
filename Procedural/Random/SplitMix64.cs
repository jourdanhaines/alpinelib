using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural {
    /// <summary>
    /// A small, fast, seeded random stream whose output is identical on every runtime, so the same seed
    /// always generates the same content on client, server and in tests.
    /// </summary>
    /// <remarks>
    /// Use this instead of <c>System.Random</c> (implementation-defined across runtimes) or
    /// <c>UnityEngine.Random</c> (global state). Independent decisions should each draw from their own
    /// <see cref="Derive"/>d stream, so adding a draw to one phase never shifts another phase's output.
    /// </remarks>
    public sealed class SplitMix64 {
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
        private const float FloatUnit = 1f / 16777216f;
        private const double DoubleUnit = 1.0 / 9007199254740992.0;

        private ulong _state;

        /// <summary>Starts a stream at <paramref name="seed"/>.</summary>
        public SplitMix64(ulong seed) {
            Seed = seed;
            _state = seed;
        }

        /// <summary>The seed this stream started from; unchanged by drawing.</summary>
        public ulong Seed { get; }

        /// <summary>The next 64 random bits.</summary>
        public ulong NextULong() {
            _state += GoldenGamma;
            ulong mixed = _state;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            return mixed ^ (mixed >> 31);
        }

        /// <summary>The next 32 random bits (the high half of <see cref="NextULong"/>).</summary>
        public uint NextUInt() {
            return (uint)(NextULong() >> 32);
        }

        /// <summary>A uniform integer in [0, <paramref name="maxExclusive"/>), free of modulo bias.</summary>
        public int NextInt(int maxExclusive) {
            if (maxExclusive <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");
            }
            return (int)NextBounded((uint)maxExclusive);
        }

        /// <summary>A uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        public int NextInt(int minInclusive, int maxExclusive) {
            long range = (long)maxExclusive - minInclusive;
            if (range <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must exceed minInclusive.");
            }
            return (int)(minInclusive + (long)NextBounded((uint)range));
        }

        /// <summary>A uniform float in [0, 1) with 24 bits of precision.</summary>
        public float NextFloat() {
            return (NextULong() >> 40) * FloatUnit;
        }

        /// <summary>A uniform double in [0, 1) with 53 bits of precision.</summary>
        public double NextDouble() {
            return (NextULong() >> 11) * DoubleUnit;
        }

        /// <summary>True with probability <paramref name="probability"/>. Always consumes one draw.</summary>
        public bool Chance(double probability) {
            return NextDouble() < probability;
        }

        /// <summary>A uniformly chosen element.</summary>
        public T Pick<T>(IReadOnlyList<T> items) {
            if (items == null) {
                throw new ArgumentNullException(nameof(items));
            }
            if (items.Count == 0) {
                throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
            }
            return items[NextInt(items.Count)];
        }

        /// <summary>
        /// An element chosen in proportion to its weight. Zero or negative weights are never chosen; at
        /// least one weight must be positive and none may be NaN or infinite.
        /// </summary>
        public T PickWeighted<T>(IReadOnlyList<T> items, Func<T, float> weightOf) {
            if (items == null) {
                throw new ArgumentNullException(nameof(items));
            }
            if (weightOf == null) {
                throw new ArgumentNullException(nameof(weightOf));
            }
            double total = TotalWeight(items, weightOf);
            double roll = NextDouble() * total;
            return SelectByCumulativeWeight(items, weightOf, roll);
        }

        /// <summary>Shuffles <paramref name="items"/> in place (Fisher–Yates).</summary>
        public void Shuffle<T>(IList<T> items) {
            if (items == null) {
                throw new ArgumentNullException(nameof(items));
            }
            for (int index = items.Count - 1; index > 0; index--) {
                int swapIndex = NextInt(index + 1);
                T held = items[index];
                items[index] = items[swapIndex];
                items[swapIndex] = held;
            }
        }

        /// <summary>
        /// An independent child stream named <paramref name="stream"/>. It depends only on
        /// <see cref="Seed"/> and the name, never on how much this stream has already drawn.
        /// </summary>
        public SplitMix64 Derive(string stream) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            }
            return new SplitMix64(StableHash.Mix(Seed, StableHash.Fnv1a64(stream)));
        }

        // Lemire's multiply-shift: rejects the few low products that would bias the result.
        private uint NextBounded(uint range) {
            ulong product = (ulong)NextUInt() * range;
            uint low = (uint)product;
            if (low >= range) {
                return (uint)(product >> 32);
            }
            uint threshold = (0u - range) % range;
            while (low < threshold) {
                product = (ulong)NextUInt() * range;
                low = (uint)product;
            }
            return (uint)(product >> 32);
        }

        private static double TotalWeight<T>(IReadOnlyList<T> items, Func<T, float> weightOf) {
            double total = 0;
            for (int index = 0; index < items.Count; index++) {
                float weight = weightOf(items[index]);
                if (float.IsNaN(weight) || float.IsInfinity(weight)) {
                    throw new ArgumentException($"Weight at index {index} is not finite.", nameof(weightOf));
                }
                total += Math.Max(0f, weight);
            }
            if (total <= 0) {
                throw new ArgumentException("At least one weight must be positive.", nameof(weightOf));
            }
            return total;
        }

        private static T SelectByCumulativeWeight<T>(IReadOnlyList<T> items, Func<T, float> weightOf, double roll) {
            double cumulative = 0;
            int lastPositive = -1;
            for (int index = 0; index < items.Count; index++) {
                float weight = weightOf(items[index]);
                if (weight <= 0f) {
                    continue;
                }
                cumulative += weight;
                lastPositive = index;
                if (roll < cumulative) {
                    return items[index];
                }
            }
            return items[lastPositive];
        }
    }
}
