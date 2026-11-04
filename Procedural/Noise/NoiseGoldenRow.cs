namespace AlpineLib.Procedural.Noise {
    /// <summary>The noise functions pinned by <see cref="NoiseGoldenTable"/>, one row of eight points each.</summary>
    public enum NoiseGoldenRow {
        GradientSample1,
        GradientSample2,
        GradientSample3,
        FbmSample1,
        FbmSample2,
        DomainWarpX,
        DomainWarpY,
        NoiseField2,
        LatticeUnit2,
    }
}
