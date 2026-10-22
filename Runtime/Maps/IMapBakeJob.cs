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

        /// <summary>Advances the job within about <paramref name="budgetMilliseconds"/> of main-thread time.</summary>
        MapBakeJobStatus Step(float budgetMilliseconds);
    }
}
