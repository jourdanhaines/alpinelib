using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>One mesh renderer a <see cref="DitherFadeGroup"/> fades: its own materials and fade delay.</summary>
    internal sealed class DitherFadeEntry {
        public DitherFadeEntry(MeshRenderer renderer, Material[] originals, Material[] twins, bool hadBlock, float delay) {
            Renderer = renderer;
            Originals = originals;
            Twins = twins;
            HasForeignBlock = hadBlock;
            Delay = delay;
        }

        public MeshRenderer Renderer { get; }

        /// <summary>The shared materials restored once the fade is done.</summary>
        public Material[] Originals { get; }

        /// <summary>The fading twins drawn meanwhile.</summary>
        public Material[] Twins { get; }

        /// <summary>Whether someone else put a property block on the renderer (before or during the fade): it is kept, never cleared.</summary>
        public bool HasForeignBlock { get; set; }

        /// <summary>Someone else replaced the renderer's materials mid-fade: it left the fade and is no longer touched.</summary>
        public bool IsReleased { get; set; }

        /// <summary>Seconds after the group's start this renderer begins to show.</summary>
        public float Delay { get; }
    }
}
