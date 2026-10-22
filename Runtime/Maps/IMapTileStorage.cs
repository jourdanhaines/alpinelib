using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Maps {
    /// <summary>Where saved tiles live between sessions. Loads and saves run off the main thread.</summary>
    public interface IMapTileStorage {
        /// <summary>True when a tile for <paramref name="cell"/> at <paramref name="level"/> has been saved.</summary>
        bool Exists(int level, CellCoord cell);

        /// <summary>The saved <paramref name="side"/>² RGBA8 texels, or null when missing or unreadable.</summary>
        Task<byte[]> LoadAsync(int level, CellCoord cell, int side);

        /// <summary>Saves a <paramref name="side"/>² RGBA8 tile, replacing any previous one.</summary>
        Task SaveAsync(int level, CellCoord cell, int side, byte[] rgba);

        /// <summary>Forgets a saved tile, if any.</summary>
        void Delete(int level, CellCoord cell);
    }
}
