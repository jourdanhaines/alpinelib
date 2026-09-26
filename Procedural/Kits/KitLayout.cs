using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Kits {
    /// <summary>
    /// An ordered list of kit placements: the engine-free output of a generator, which a Unity-side
    /// assembler turns into objects.
    /// </summary>
    public sealed class KitLayout {
        private readonly List<KitPlacement> _placements = new List<KitPlacement>();

        /// <summary>Placements in the order they were added.</summary>
        public IReadOnlyList<KitPlacement> Placements => _placements;

        /// <summary>Number of placements.</summary>
        public int Count => _placements.Count;

        /// <summary>Appends a placement.</summary>
        public void Add(KitPlacement placement) {
            if (placement == null) {
                throw new ArgumentNullException(nameof(placement));
            }
            _placements.Add(placement);
        }

        /// <summary>Appends and returns a new placement.</summary>
        public KitPlacement Add(string partName, string instanceName, string group, KitPose pose) {
            var placement = new KitPlacement(partName, instanceName, group, pose);
            _placements.Add(placement);
            return placement;
        }

        /// <summary>Removes every placement.</summary>
        public void Clear() {
            _placements.Clear();
        }

        /// <summary>
        /// A fingerprint of the layout, stable across runs and runtimes: FNV-1a over each placement's
        /// names (UTF-8, length-prefixed) and pose float bits, in placement order. Order-sensitive.
        /// </summary>
        public ulong ComputeHash() {
            ulong hash = StableHash.Fnv1a64Append(StableHash.Fnv1a64OffsetBasis, (uint)_placements.Count);
            foreach (KitPlacement placement in _placements) {
                hash = AppendPlacement(hash, placement);
            }
            return hash;
        }

        private static ulong AppendPlacement(ulong hash, KitPlacement placement) {
            hash = AppendString(hash, placement.PartName);
            hash = AppendString(hash, placement.InstanceName);
            hash = AppendString(hash, placement.Group);
            hash = AppendFloat(hash, placement.Pose.Position.X);
            hash = AppendFloat(hash, placement.Pose.Position.Y);
            hash = AppendFloat(hash, placement.Pose.Position.Z);
            return AppendFloat(hash, placement.Pose.YawDegrees);
        }

        // Length prefix keeps ("ab","c") and ("a","bc") apart.
        private static ulong AppendString(ulong hash, string text) {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            hash = StableHash.Fnv1a64Append(hash, (uint)bytes.Length);
            return StableHash.Fnv1a64Append(hash, bytes);
        }

        // -0 is folded into +0 so a sign-only difference from arithmetic does not change the hash.
        private static ulong AppendFloat(ulong hash, float value) {
            float normalised = value == 0f ? 0f : value;
            return StableHash.Fnv1a64Append(hash, unchecked((uint)BitConverter.SingleToInt32Bits(normalised)));
        }
    }
}
