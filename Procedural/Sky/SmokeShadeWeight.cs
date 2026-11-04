namespace AlpineLib.Procedural.Sky {
    /// <summary>One shade of a <see cref="SmokeStage"/>: an index into <see cref="SmokeStyle.Shades"/> and its relative weight.</summary>
    public readonly struct SmokeShadeWeight {
        public SmokeShadeWeight(int shade, double weight) {
            Shade = shade;
            Weight = weight;
        }

        public int Shade { get; }
        public double Weight { get; }
    }
}
