using System;
using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>Look of one kind of map marker: frame and arrow sprites, sizes in canvas units, portrait cutout, draw priority.</summary>
    [Serializable]
    public class MapMarkerStyle {
        [Tooltip("Ring drawn over the portrait.")]
        [SerializeField] private Sprite frameSprite;

        [Tooltip("Arrow shown on the edge while the target is off-view; points along +X.")]
        [SerializeField] private Sprite arrowSprite;

        [Tooltip("Marker (frame) side, canvas units.")]
        [SerializeField] private float size = 64f;

        [Tooltip("Arrow width, canvas units (height follows the sprite's aspect).")]
        [SerializeField] private float arrowSize = 32f;

        [Tooltip("Portrait circle diameter as a fraction of the marker side.")]
        [SerializeField] private float cutoutDiameter = 0.755f;

        [Tooltip("Higher draws on top.")]
        [SerializeField] private int priority;

        /// <summary>Ring drawn over the portrait.</summary>
        public Sprite FrameSprite { get => frameSprite; set => frameSprite = value; }

        /// <summary>Arrow sprite, pointing along +X.</summary>
        public Sprite ArrowSprite { get => arrowSprite; set => arrowSprite = value; }

        /// <summary>Marker side, canvas units.</summary>
        public float Size { get => size; set => size = value; }

        /// <summary>Arrow width, canvas units.</summary>
        public float ArrowSize { get => arrowSize; set => arrowSize = value; }

        /// <summary>Portrait circle diameter as a fraction of the marker side.</summary>
        public float CutoutDiameter { get => cutoutDiameter; set => cutoutDiameter = value; }

        /// <summary>Higher draws on top.</summary>
        public int Priority { get => priority; set => priority = value; }

        /// <summary>Arrow height for the sprite's aspect.</summary>
        public float ArrowHeight() {
            if (arrowSprite == null || arrowSprite.rect.width <= 0f) return arrowSize;

            return arrowSize * arrowSprite.rect.height / arrowSprite.rect.width;
        }
    }
}
