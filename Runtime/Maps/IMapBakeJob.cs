using System;
using AlpineLib.Procedural.Grid;
using AlpineLib.Rendering;

namespace AlpineLib.Maps {
    /// <summary>
    /// One cell being made ready for capture (an off-screen build, or a check that a live cell is complete).
    /// Disposed by the queue once captured or failed, which is where off-screen builds are torn down.
    /// </summary>
    public interface IMapBakeJob : IDisposable {
        /// <summary>Cell being baked.</summary>
        CellCoord Cell { get; }

        /// <summary>World area the capture covers; valid once <see cref="Step"/> reports ready.</summary>
        TopDownCaptureArea CaptureArea { get; }

        /// <summary>
        /// True when the capture may only fill levels the cell has no tile at: a worse bake (e.g. an
        /// off-screen build re-making a dropped fine level) must not overwrite a better saved tile.
        /// </summary>
        bool KeepsExistingTiles { get; }

        /// <summary>Advances the job within about <paramref name="budgetMilliseconds"/> of main-thread time.</summary>
        MapBakeJobStatus Step(float budgetMilliseconds);
    }
}
