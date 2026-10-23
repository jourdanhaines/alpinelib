using AlpineLib.Procedural.Grid;
using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>Hands captures to a <see cref="MapTileStore"/> through <see cref="MapTileStore.AcceptMissing"/>: existing tiles are kept.</summary>
    public sealed class MapTileFillSink : IMapTileSink {
        private readonly MapTileStore _store;

        public MapTileFillSink(MapTileStore store) {
            _store = store;
        }

        public void Accept(CellCoord cell, RenderTexture capture) {
            _store?.AcceptMissing(cell, capture);
        }
    }
}
