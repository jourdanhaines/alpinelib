using System;
using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// The stylized shader's screen-door fade: the <c>_DITHER_FADE</c> keyword and its <c>_FadeAmount</c> (0 hidden,
    /// 1 shown; per instance when drawn instanced), plus the clock fades run on by default.
    /// </summary>
    public static class DitherFade {
        public const string Keyword = "_DITHER_FADE";
        public const string AmountName = "_FadeAmount";
        public const string ToggleName = "_DitherFade";

        public static readonly int AmountId = Shader.PropertyToID(AmountName);
        public static readonly int ToggleId = Shader.PropertyToID(ToggleName);

        /// <summary>Game time in play mode, real time in the editor (previews keep moving between repaints).</summary>
        public static readonly Func<double> DefaultClock = () => Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;

        /// <summary>True when <paramref name="material"/>'s shader has the fade keyword.</summary>
        public static bool Supports(Material material) {
            if (material == null || material.shader == null) return false;

            return material.shader.keywordSpace.FindKeyword(Keyword).isValid;
        }
    }
}
