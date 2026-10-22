using System;
using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>One tile level: texel side per tile, how many tiles stay resident, and whether tiles are saved.</summary>
    [Serializable]
    public class MapTileLevelSettings {
        [Tooltip("Tile side, texels; a power of two no larger than the capture size.")]
        [SerializeField] private int tileSize;

        [Tooltip("Resident tiles (Texture2DArray slices).")]
        [SerializeField] private int budget;

        [Tooltip("Save accepted tiles to storage and reload them instead of re-baking.")]
        [SerializeField] private bool persist;

        /// <summary>Builds a level.</summary>
        public MapTileLevelSettings(int tileSize, int budget, bool persist) {
            this.tileSize = tileSize;
            this.budget = budget;
            this.persist = persist;
        }

        /// <summary>Tile side, texels.</summary>
        public int TileSize => tileSize;

        /// <summary>Resident tiles.</summary>
        public int Budget => budget;

        /// <summary>True when tiles are saved and reloaded.</summary>
        public bool Persist => persist;
    }
}
