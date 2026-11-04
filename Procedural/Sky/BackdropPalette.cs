namespace AlpineLib.Procedural.Sky {
    /// <summary>The atlas strips backdrop silhouettes are painted from: near and far rock, and a cloud bank's top and underside.</summary>
    public sealed class BackdropPalette {
        public BackdropPalette(SkyAtlasBand near, SkyAtlasBand far, SkyAtlasBand cloudTop, SkyAtlasBand cloudUnderside) {
            Near = near;
            Far = far;
            CloudTop = cloudTop;
            CloudUnderside = cloudUnderside;
        }

        /// <summary>Warmer rock, painted on even variants.</summary>
        public SkyAtlasBand Near { get; }

        /// <summary>Hazier rock, painted on odd variants.</summary>
        public SkyAtlasBand Far { get; }

        public SkyAtlasBand CloudTop { get; }
        public SkyAtlasBand CloudUnderside { get; }

        /// <summary>The rock strip of <paramref name="variant"/>.</summary>
        public SkyAtlasBand RockFor(int variant) {
            return variant % 2 == 0 ? Near : Far;
        }
    }
}
