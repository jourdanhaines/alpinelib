using System;

namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// A ready-to-sample 2D scalar field: optional domain warp followed by fBm, all under one seed.
    /// </summary>
    /// <remarks>
    /// An immutable value: copy it to worker threads freely. The warp seed is derived from
    /// <see cref="Seed"/>, so two fields with different seeds share nothing.
    /// </remarks>
    public readonly struct NoiseField2 {
        private const ulong WarpSalt = 0x57415250UL;

        /// <summary>
        /// A field of <paramref name="settings"/> fBm under <paramref name="seed"/>, warped by
        /// <paramref name="warpAmplitude"/> units of <paramref name="warpSettings"/> fBm (no warp when the
        /// amplitude is 0). Throws when either settings value in use has no octaves.
        /// </summary>
        public NoiseField2(ulong seed, FbmSettings settings, double warpAmplitude = 0.0, FbmSettings warpSettings = default) {
            if (settings.Octaves < 1) {
                throw new ArgumentException("Settings has no octaves.", nameof(settings));
            }
            if (warpAmplitude != 0.0 && warpSettings.Octaves < 1) {
                throw new ArgumentException("A non-zero warp amplitude needs warp settings with octaves.", nameof(warpSettings));
            }
            Seed = seed;
            Settings = settings;
            WarpAmplitude = warpAmplitude;
            WarpSettings = warpSettings;
        }

        /// <summary>The seed the fBm samples with.</summary>
        public ulong Seed { get; }

        /// <summary>The fBm summed at the warped point.</summary>
        public FbmSettings Settings { get; }

        /// <summary>The most a sample point is moved along each axis before sampling.</summary>
        public double WarpAmplitude { get; }

        /// <summary>The fBm that drives the warp offsets.</summary>
        public FbmSettings WarpSettings { get; }

        /// <summary>The field at (<paramref name="x"/>, <paramref name="y"/>), in [−1, 1].</summary>
        public double Sample(double x, double y) {
            if (WarpAmplitude != 0.0) {
                DomainWarp.Warp2(StableHash.Mix(Seed, WarpSalt), ref x, ref y, WarpAmplitude, WarpSettings);
            }
            return Fbm.Sample2(Seed, x, y, Settings);
        }

        /// <summary>The field remapped to [0, 1].</summary>
        public double Sample01(double x, double y) {
            return Sample(x, y) * 0.5 + 0.5;
        }
    }
}
