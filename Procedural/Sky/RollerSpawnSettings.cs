using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of where and how often wind-rolled props appear (<see cref="RollerSpawner"/>), how many roll at once,
    /// and which a viewer is shown being born and breaking up (births and life ends stay out of view).
    /// </summary>
    public sealed class RollerSpawnSettings {
        /// <summary>Mean spawns per cell per minute of the wind's mean drift.</summary>
        public double PerCellPerMinute { get; set; } = 0.25;

        /// <summary>Drift metres one spawn draw covers (a Poisson count per cell and bucket; each cell's buckets are offset by a seeded phase).</summary>
        public double BucketDrift { get; set; } = 120.0;

        /// <summary>Spawn points (uniform over the cell) tried in turn until one is clear of the footprints.</summary>
        public int PointTries { get; set; } = 4;

        /// <summary>Gap kept between a spawn point and its cell's edge, metres.</summary>
        public double EdgeMargin { get; set; } = 1.0;

        /// <summary>Most props of one spawn cell alive at once (admitted ones, by their whole life).</summary>
        public int MaxPerCell { get; set; } = 6;

        /// <summary>Most props drawn at once in the whole window.</summary>
        public int MaxLive { get; set; } = 40;

        /// <summary>
        /// A spawn is shown being born only when its cell's centre lies at least this far upwind of the viewer, metres, or
        /// past <see cref="HiddenBirthRadius"/> from it; one born nearer is never drawn by that viewer.
        /// </summary>
        public double VisibleBirthUpwind { get; set; } = 150.0;
        public double HiddenBirthRadius { get; set; } = 250.0;

        /// <summary>
        /// A prop whose life would end within the viewer's draw distance rolls on, <see cref="OverrunStep"/> metres at a
        /// time and at most <see cref="MostOverrun"/> in all, asked for <see cref="OverrunLead"/> metres before its end.
        /// </summary>
        public double OverrunStep { get; set; } = 200.0;
        public double MostOverrun { get; set; } = 600.0;
        public double OverrunLead { get; set; } = 40.0;

        public static RollerSpawnSettings Default => new RollerSpawnSettings();

        public void Validate() {
            if (!(PerCellPerMinute >= 0.0) || !(BucketDrift > 0.0)) throw new ArgumentException("The spawn rate must not be negative and the bucket must be positive.");
            if (PointTries < 1) throw new ArgumentException("Point tries must be at least 1.");
            if (MaxPerCell < 0 || MaxLive < 0 || EdgeMargin < 0.0) throw new ArgumentException("Caps and the edge margin must not be negative.");
            if (VisibleBirthUpwind < 0.0 || HiddenBirthRadius < 0.0) throw new ArgumentException("Birth distances must not be negative.");
            if (OverrunStep < 0.0 || MostOverrun < 0.0 || OverrunLead < 0.0) throw new ArgumentException("Overrun lengths must not be negative.");
        }
    }
}
