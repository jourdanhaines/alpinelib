using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>One mesh renderer a <see cref="DitherFadeGroup"/> fades: its own materials and fade delay.</summary>
    internal readonly struct DitherFadeEntry {
        public DitherFadeEntry(MeshRenderer renderer, Material[] originals, Material[] twins, bool hadBlock, float delay) {
            Renderer = renderer;
            Originals = originals;
            Twins = twins;
            HadBlock = hadBlock;
            Delay = delay;
        }

        public MeshRenderer Renderer { get; }

        /// <summary>The shared materials restored once the fade is done.</summary>
        public Material[] Originals { get; }

        /// <summary>The fading twins drawn meanwhile.</summary>
        public Material[] Twins { get; }

        /// <summary>Whether the renderer carried a property block of its own before the fade.</summary>
        public bool HadBlock { get; }

        /// <summary>Seconds after the group's start this renderer begins to show.</summary>
        public float Delay { get; }
    }
}
