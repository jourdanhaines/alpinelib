using System;

namespace AlpineLib.Maps {
    /// <summary>Which tile level a zoom draws from, and at which of its mips.</summary>
    public static class MapTileLevels {
        /// <summary>
        /// The finest level whose texel density is at most twice <paramref name="pixelsPerMetre"/>
        /// (screen pixels per map metre), else the coarsest. With 128 m cells and 1024/256/64 tiles:
        /// L0 from 4 px/m, L1 from 1 px/m, L2 below.
        /// </summary>
        public static int Select(double pixelsPerMetre, MapTileSettings settings) {
            for (int level = 0; level < settings.LevelCount; level++) {
                if (pixelsPerMetre * 2.0 >= TexelsPerMetre(level, settings)) return level;
            }

            return settings.LevelCount - 1;
        }

        /// <summary>Texels per metre of level <paramref name="level"/>'s top mip.</summary>
        public static double TexelsPerMetre(int level, MapTileSettings settings) {
            return settings.Level(level).TileSize / settings.CellSize;
        }

        /// <summary>Mip (0..<see cref="MapTileSettings.MipsPerLevel"/>−1, fractional) that level <paramref name="level"/> is sampled at for <paramref name="pixelsPerMetre"/>.</summary>
        public static float Lod(int level, double pixelsPerMetre, MapTileSettings settings) {
            double ratio = TexelsPerMetre(level, settings) / Math.Max(1e-6, pixelsPerMetre);
            double lod = Math.Log(Math.Max(ratio, 1.0), 2.0);
            return (float)Math.Min(lod, MapTileSettings.MipsPerLevel - 1);
        }
    }
}
