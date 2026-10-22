using AlpineLib.Procedural.Grid;
using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>Receives a freshly captured cell tile: a square sRGB render texture with a full mip chain.</summary>
    public interface IMapTileSink {
        /// <summary>Takes <paramref name="cell"/>'s tile from <paramref name="capture"/>; the texture is reused after the call returns.</summary>
        void Accept(CellCoord cell, RenderTexture capture);
    }
}
