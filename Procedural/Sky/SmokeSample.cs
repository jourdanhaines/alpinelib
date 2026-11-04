namespace AlpineLib.Procedural.Sky {
    /// <summary>How one puff looks, drawn from a <see cref="SmokeStyle"/> at an efficiency.</summary>
    public readonly struct SmokeSample {
        public SmokeSample(int shade, double thickness, double lifeScale, double riseScale) {
            Shade = shade;
            Thickness = thickness;
            LifeScale = lifeScale;
            RiseScale = riseScale;
        }

        /// <summary>Index into <see cref="SmokeStyle.Shades"/>.</summary>
        public int Shade { get; }

        /// <summary>Size multiplier (also biases the puff toward its lumpier shapes).</summary>
        public double Thickness { get; }

        /// <summary>Lifetime and rise multipliers.</summary>
        public double LifeScale { get; }
        public double RiseScale { get; }
    }
}
