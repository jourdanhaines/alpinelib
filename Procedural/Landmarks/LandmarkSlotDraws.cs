namespace AlpineLib.Procedural.Landmarks {
    /// <summary>One fixed cluster member slot's draws, taken in a fixed order.</summary>
    internal readonly struct LandmarkSlotDraws {
        private LandmarkSlotDraws(double size, double bearing, double ring, ulong shapeSeed, LandmarkDraws spare) {
            Size = size;
            Bearing = bearing;
            Ring = ring;
            ShapeSeed = shapeSeed;
            Spare = spare;
        }

        public double Size { get; }

        public double Bearing { get; }

        public double Ring { get; }

        public ulong ShapeSeed { get; }

        public LandmarkDraws Spare { get; }

        public static LandmarkSlotDraws Draw(SplitMix64 random) {
            double size = random.NextDouble();
            double bearing = random.NextDouble();
            double ring = random.NextDouble();
            ulong shapeSeed = random.NextULong();
            var spare = new LandmarkDraws(LandmarkField.Quantise(random.NextDouble()), LandmarkField.Quantise(random.NextDouble()),
                                          LandmarkField.Quantise(random.NextDouble()), LandmarkField.Quantise(random.NextDouble()));
            return new LandmarkSlotDraws(size, bearing, ring, shapeSeed, spare);
        }
    }
}
