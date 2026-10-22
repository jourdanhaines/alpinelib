using AlpineLib.Procedural.Grid;

namespace AlpineLib.Maps {
    /// <summary>Something that can make a cell ready for a map capture: the live world, or an off-screen builder.</summary>
    public interface IMapCellBakeSource {
        /// <summary>What baking <paramref name="cell"/> would cost from this source right now.</summary>
        MapBakeCost GetCost(CellCoord cell);

        /// <summary>Starts baking <paramref name="cell"/>; null when the source cannot after all.</summary>
        IMapBakeJob Begin(CellCoord cell);
    }
}
