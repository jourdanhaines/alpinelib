using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The world a wind-rolled prop crosses, in the roll's own plane (absolute x East, z North, metres): what blocks
    /// its ball, what the ball rests on and the surface height under a point. Must be safe to read from a worker.
    /// </summary>
    public interface IRollerWorld {
        /// <summary>Whether a ball of <paramref name="radius"/> centred over (<paramref name="x"/>, <paramref name="z"/>) may be there.</summary>
        RollerBlock BlockAt(double x, double z, double radius);

        /// <summary>What a ball of <paramref name="radius"/> centred over (<paramref name="x"/>, <paramref name="z"/>) rests on.</summary>
        RollerSupport SupportAt(double x, double z, double radius);

        /// <summary>Height of the surface straight under (<paramref name="x"/>, <paramref name="z"/>), negative infinity where nothing is known.</summary>
        double SurfaceAt(double x, double z);

        /// <summary>
        /// Adds the round stops of a ball of <paramref name="radius"/> whose edge lies within <paramref name="reach"/>
        /// metres of (<paramref name="x"/>, <paramref name="z"/>); false when part of that reach is not loaded (the
        /// answer may grow once it is).
        /// </summary>
        bool StopsNear(double x, double z, double radius, double reach, List<RollerDisc> into);
    }
}
