namespace AlpineLib.Procedural.Sky {
    /// <summary>What a <see cref="CloudLandmarkSweep.Clearance"/> saw: the least gap between a cloud base and a landmark top under it, and the most lift.</summary>
    public sealed class CloudClearance {
        /// <summary>Least (cloud base − landmark top) over every overlapping sample, metres; infinite when none overlapped.</summary>
        public double Least { get; private set; } = double.PositiveInfinity;

        /// <summary>Most lift any overlapping cloud took, metres.</summary>
        public double MostLift { get; private set; }

        /// <summary>Cloud samples whose drawn footprint overlapped a hull.</summary>
        public int Overlaps { get; private set; }

        /// <summary>Records one overlapping sample.</summary>
        public void Add(double clearance, double lift) {
            Overlaps++;
            if (clearance < Least) Least = clearance;
            if (lift > MostLift) MostLift = lift;
        }

        /// <summary>Folds another sweep's samples in.</summary>
        public void Merge(CloudClearance other) {
            if (other == null || other.Overlaps == 0) return;

            Overlaps += other.Overlaps;
            if (other.Least < Least) Least = other.Least;
            if (other.MostLift > MostLift) MostLift = other.MostLift;
        }
    }
}
