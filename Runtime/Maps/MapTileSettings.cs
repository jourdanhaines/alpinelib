using System;
using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>
    /// Tile store layout: the capture size, the cell size in metres, and one entry per level, finest
    /// first. Each level keeps <see cref="MipsPerLevel"/> mips, copied from the capture's mip chain
    /// starting at the mip whose size equals the level's tile size.
    /// </summary>
    [Serializable]
    public class MapTileSettings {
        /// <summary>Mips kept per level.</summary>
        public const int MipsPerLevel = 2;

        [Tooltip("Captured tile side, texels (power of two).")]
        [SerializeField] private int captureSize = 1024;

        [Tooltip("Cell side, metres.")]
        [SerializeField] private float cellSize = 128f;

        [Tooltip("Levels, finest first.")]
        [SerializeField] private MapTileLevelSettings[] levels = {
            new MapTileLevelSettings(1024, 20, false),
            new MapTileLevelSettings(256, 160, true),
            new MapTileLevelSettings(64, 512, true),
        };

        /// <summary>The stock layout: 1024² capture, 128 m cells, L0 1024²×20 (not saved), L1 256²×160, L2 64²×512.</summary>
        public static MapTileSettings Default => new MapTileSettings();

        /// <summary>Captured tile side, texels.</summary>
        public int CaptureSize => captureSize;

        /// <summary>Cell side, metres.</summary>
        public double CellSize => cellSize;

        /// <summary>Number of levels.</summary>
        public int LevelCount => levels.Length;

        /// <summary>Level <paramref name="level"/>'s settings.</summary>
        public MapTileLevelSettings Level(int level) {
            return levels[level];
        }

        /// <summary>Capture mip whose size equals level <paramref name="level"/>'s tile size.</summary>
        public int CaptureMipOf(int level) {
            int mip = 0;
            for (int size = captureSize; size > levels[level].TileSize; size >>= 1) mip++;
            return mip;
        }

        /// <summary>Resident bytes of every level array (RGBA8, <see cref="MipsPerLevel"/> mips each).</summary>
        public long ResidentBytes() {
            long total = 0;
            foreach (MapTileLevelSettings level in levels) {
                for (int mip = 0; mip < MipsPerLevel; mip++) {
                    long side = Math.Max(1, level.TileSize >> mip);
                    total += side * side * 4L * level.Budget;
                }
            }

            return total;
        }
    }
}
