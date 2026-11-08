using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>A world without landmarks: the sky draws exactly as it would without a provider.</summary>
    public sealed class NoLandmarkSkyProvider : ILandmarkSkyProvider {
        public static readonly NoLandmarkSkyProvider Instance = new NoLandmarkSkyProvider();

        private NoLandmarkSkyProvider() {
        }

        public bool TryGetNearby(double x, double z, double radius, double liftRadius, List<LandmarkSkyShape> shapes) {
            return true;
        }
    }
}
