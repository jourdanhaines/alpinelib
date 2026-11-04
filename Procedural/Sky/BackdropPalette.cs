namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The atlas strips backdrop silhouettes are painted from: near and far rock, a cloud bank's top and underside,
    /// and the ground annulus under the ring.
    /// </summary>
    public sealed class BackdropPalette {
        public BackdropPalette(SkyAtlasBand near, SkyAtlasBand far, SkyAtlasBand cloudTop, SkyAtlasBand cloudUnderside, SkyAtlasBand ground) {
            Near = near;
            Far = far;
            CloudTop = cloudTop;
            CloudUnderside = cloudUnderside;
            Ground = ground;
        }

        /// <summary>Warmer rock, painted on even variants.</summary>
        public SkyAtlasBand Near { get; }

        /// <summary>Hazier rock, painted on odd variants.</summary>
        public SkyAtlasBand Far { get; }

        public SkyAtlasBand CloudTop { get; }
        public SkyAtlasBand CloudUnderside { get; }

        /// <summary>The ground annulus is painted from the middle of this strip.</summary>
        public SkyAtlasBand Ground { get; }

        /// <summary>The rock strip of <paramref name="variant"/>.</summary>
        public SkyAtlasBand RockFor(int variant) {
            return variant % 2 == 0 ? Near : Far;
        }
    }
}
