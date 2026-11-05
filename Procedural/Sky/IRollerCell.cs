using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One streamed cell's part of an <see cref="IRollerWorld"/>, in the cell's local plane (origin at the cell's centre).
    /// <see cref="RollerCellWorld"/> routes each point to the cell it lies in. Must be safe to read from a worker.
    /// </summary>
    /// <remarks>Whether a standing prop stops or is hopped is the cell's call for the asking ball's radius (big props hop more).</remarks>
    public interface IRollerCell {
        RollerBlock BlockAt(double x, double z, double radius);

        RollerSupport SupportAt(double x, double z, double radius);

        double SurfaceAt(double x, double z);

        /// <summary>
        /// Adds the round stops of a ball of <paramref name="radius"/> whose edge lies within <paramref name="reach"/>
        /// metres of (<paramref name="x"/>, <paramref name="z"/>), in the cell's plane. Stops that are not round (a
        /// station) only answer <see cref="BlockAt"/>.
        /// </summary>
        void StopsNear(double x, double z, double radius, double reach, List<RollerDisc> into);
    }
}
