using System;
using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>A shared material and its committed fading template (the same material with the fade keyword on).</summary>
    [Serializable]
    public struct DitherFadeTemplate {
        [Tooltip("The shared material things draw with once faded in.")]
        public Material source;
        [Tooltip("The source with the fade keyword on; shipping it keeps the fading variant in player builds.")]
        public Material template;
    }
}
