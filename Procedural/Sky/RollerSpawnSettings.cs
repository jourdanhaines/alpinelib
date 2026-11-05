using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>Tunables of where and how often wind-rolled props appear (<see cref="RollerSpawner"/>) and how many roll at once.</summary>
    public sealed class RollerSpawnSettings {
        /// <summary>Mean spawns per cell per minute of the wind's mean drift.</summary>
        public double PerCellPerMinute { get; set; } = 2.0;

        /// <summary>Drift metres one spawn draw covers (a Poisson count per cell and bucket).</summary>
        public double BucketDrift { get; set; } = 120.0;

        /// <summary>Uniform points drawn per spawn point; the most upwind is taken, so most props enter from the upwind side.</summary>
        public int UpwindPicks { get; set; } = 3;

        /// <summary>Spawn points tried in turn until one is clear of the footprints.</summary>
        public int PointTries { get; set; } = 4;

        /// <summary>Gap kept between a spawn point and its cell's edge, metres.</summary>
        public double EdgeMargin { get; set; } = 1.0;

        /// <summary>Most props of one spawn cell alive at once.</summary>
        public int MaxPerCell { get; set; } = 6;

        /// <summary>Most props drawn at once in the whole window.</summary>
        public int MaxLive { get; set; } = 40;

        public static RollerSpawnSettings Default => new RollerSpawnSettings();

        public void Validate() {
            if (!(PerCellPerMinute >= 0.0) || !(BucketDrift > 0.0)) throw new ArgumentException("The spawn rate must not be negative and the bucket must be positive.");
            if (UpwindPicks < 1 || PointTries < 1) throw new ArgumentException("Upwind picks and point tries must be at least 1.");
            if (MaxPerCell < 0 || MaxLive < 0 || EdgeMargin < 0.0) throw new ArgumentException("Caps and the edge margin must not be negative.");
        }
    }
}
