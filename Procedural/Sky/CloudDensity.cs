namespace AlpineLib.Procedural.Sky {
    /// <summary>What a <see cref="CloudLandmarkSweep.Density"/> counted: clouds (by envelope) and area in full giant zones and in open sky.</summary>
    public sealed class CloudDensity {
        public double ZoneClouds { get; set; }
        public double ZoneArea { get; set; }
        public double OpenClouds { get; set; }
        public double OpenArea { get; set; }

        /// <summary>Drawn footprint area of the counted clouds, m² (each disc of its drawn radius).</summary>
        public double ZoneCover { get; set; }
        public double OpenCover { get; set; }

        /// <summary>Clouds per km² in giant zones over clouds per km² in open sky; NaN without both areas.</summary>
        public double Ratio => ZoneArea > 0.0 && OpenArea > 0.0 && OpenClouds > 0.0 ? (ZoneClouds / ZoneArea) / (OpenClouds / OpenArea) : double.NaN;

        /// <summary>Share of the sky covered in giant zones over the share covered in open sky (footprint area per km²); NaN without both.</summary>
        public double CoverRatio => ZoneArea > 0.0 && OpenArea > 0.0 && OpenCover > 0.0 ? (ZoneCover / ZoneArea) / (OpenCover / OpenArea) : double.NaN;

        /// <summary>Folds another count in.</summary>
        public void Merge(CloudDensity other) {
            if (other == null) return;

            ZoneClouds += other.ZoneClouds;
            ZoneArea += other.ZoneArea;
            OpenClouds += other.OpenClouds;
            OpenArea += other.OpenArea;
            ZoneCover += other.ZoneCover;
            OpenCover += other.OpenCover;
        }
    }
}
