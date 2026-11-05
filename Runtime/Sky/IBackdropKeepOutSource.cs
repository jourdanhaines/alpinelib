using System.Collections.Generic;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Sky {
    /// <summary>The loaded cells the backdrop ring keeps clear of, in absolute cell indices (cell x spans [x, x + 1) cell sizes).</summary>
    public interface IBackdropKeepOutSource {
        /// <summary>Changes whenever the set of cells changes.</summary>
        int KeepOutRevision { get; }

        /// <summary>Appends every loaded cell.</summary>
        void CollectKeepOutCells(List<CellCoord> cells);
    }
}
