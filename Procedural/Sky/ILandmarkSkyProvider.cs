using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The landmarks a client-side sky reacts to, in absolute xz (metres, the frame that never rebases): the cloud
    /// field lifts its clouds over them and gathers clouds about giants. Must answer the same for every client of a seed.
    /// </summary>
    public interface ILandmarkSkyProvider {
        /// <summary>
        /// Adds every landmark whose hull comes within <paramref name="radius"/> of (<paramref name="x"/>,
        /// <paramref name="z"/>) to <paramref name="shapes"/>; false when some are not known yet (ask again later). Every
        /// landmark within <paramref name="liftRadius"/> must be known on return (clouds there may need lifting now);
        /// beyond it a provider may resolve a few per call, nearest first.
        /// </summary>
        bool TryGetNearby(double x, double z, double radius, double liftRadius, List<LandmarkSkyShape> shapes);
    }
}
