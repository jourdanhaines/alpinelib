using System;

namespace AlpineLib.Sky {
    /// <summary>Tunables of <see cref="CloudShadowCookie"/>: the cookie's texels and reach, and how dark and soft a puff's shadow is.</summary>
    public sealed class CloudShadowSettings {
        /// <summary>Texels along each side of the square cookie.</summary>
        public int Resolution { get; set; } = 512;

        /// <summary>Metres the cookie spans along each light-space axis, centred on the camera's ground point.</summary>
        public float CookieSize { get; set; } = 800f;

        /// <summary>Share of the sun left under a cloud's core (1 = no shadow).</summary>
        public float ShadowLight { get; set; } = 0.55f;

        /// <summary>Outer share of the cookie over which shadows fade to none, so its edge never shows.</summary>
        public float EdgeShare { get; set; } = 0.15f;

        /// <summary>Outer share of a puff's footprint over which its shadow softens to none.</summary>
        public float Softness { get; set; } = 0.6f;

        /// <summary>Puff footprint radius times this is the radius it shadows.</summary>
        public float FootprintScale { get; set; } = 1f;

        public static CloudShadowSettings Default => new CloudShadowSettings();

        /// <summary>Metres per texel.</summary>
        public float TexelSize => CookieSize / Resolution;

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (Resolution < 16 || Resolution > 4096) throw new ArgumentOutOfRangeException(nameof(Resolution), Resolution, "Must be in [16, 4096].");
            if (!(CookieSize > 0f)) throw new ArgumentOutOfRangeException(nameof(CookieSize), CookieSize, "Must be positive.");
            if (!(ShadowLight >= 0f && ShadowLight <= 1f)) throw new ArgumentOutOfRangeException(nameof(ShadowLight), ShadowLight, "Must be in [0, 1].");
            if (!(EdgeShare > 0f && EdgeShare <= 1f)) throw new ArgumentOutOfRangeException(nameof(EdgeShare), EdgeShare, "Must be in (0, 1].");
            if (!(Softness > 0f && Softness <= 1f)) throw new ArgumentOutOfRangeException(nameof(Softness), Softness, "Must be in (0, 1].");
            if (!(FootprintScale > 0f)) throw new ArgumentOutOfRangeException(nameof(FootprintScale), FootprintScale, "Must be positive.");
        }
    }
}
