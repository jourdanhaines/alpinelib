using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// A 64-bit fingerprint of a scatter's regular clouds (anchors, altitudes, scales, meshes, headings) over a square of
    /// field cells of every class, so gates can pin the open sky bit for bit. Latent clouds are left out.
    /// </summary>
    public static class CloudFieldFingerprint {
        /// <summary>The fingerprint of the cells [-<paramref name="half"/>, <paramref name="half"/>) of every class of <paramref name="scatter"/>.</summary>
        public static ulong Of(CloudFieldScatter scatter, int half) {
            if (scatter == null) throw new ArgumentNullException(nameof(scatter));

            ulong hash = 0x434C4F5544UL;
            for (int classIndex = 0; classIndex < CloudFieldSettings.ClassCount; classIndex++) {
                for (long cellZ = -half; cellZ < half; cellZ++) {
                    for (long cellX = -half; cellX < half; cellX++) hash = MixCell(hash, scatter.Cell((CloudClass)classIndex, cellX, cellZ));
                }
            }
            return hash;
        }

        private static ulong MixCell(ulong hash, CloudInstance[] clouds) {
            ulong regular = 0;
            foreach (CloudInstance cloud in clouds) {
                if (cloud.IsLatent) continue;

                regular++;
                hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(cloud.AnchorX));
                hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(cloud.AnchorZ));
                hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(cloud.Altitude));
                hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(cloud.Scale));
                hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(cloud.Yaw));
                hash = StableHash.Mix(hash, (ulong)cloud.MeshIndex);
            }
            return StableHash.Mix(hash, regular);
        }
    }
}
