namespace AlpineLib.Procedural.Sky {
    /// <summary>The atlas strips a drifting cloud is painted from: its sunlit top and its soft underside.</summary>
    public sealed class CloudPalette {
        public CloudPalette(SkyAtlasBand top, SkyAtlasBand underside) {
            Top = top;
            Underside = underside;
        }

        public SkyAtlasBand Top { get; }
        public SkyAtlasBand Underside { get; }
    }
}
