namespace AlpineLib.Procedural.Sky {
    /// <summary>What a <see cref="CloudLandmarkSweep.Dissolve"/> saw: drawn clouds overlapping a hull that blocks them, and the fastest dissolve change.</summary>
    public sealed class CloudDissolveCheck {
        /// <summary>Drawn cloud samples whose footprint overlapped a blocking hull (must be 0).</summary>
        public int Overlaps { get; private set; }

        /// <summary>Drawn cloud samples looked at.</summary>
        public int Drawn { get; private set; }

        /// <summary>Fastest change of a cloud's dissolve share, per second.</summary>
        public double MostRate { get; private set; }

        public void AddDrawn(bool isOverlapping) {
            Drawn++;
            Overlaps += isOverlapping ? 1 : 0;
        }

        public void AddRate(double rate) {
            if (rate > MostRate) MostRate = rate;
        }

        /// <summary>Folds another sweep's samples in.</summary>
        public void Merge(CloudDissolveCheck other) {
            if (other == null) return;

            Overlaps += other.Overlaps;
            Drawn += other.Drawn;
            if (other.MostRate > MostRate) MostRate = other.MostRate;
        }
    }
}
