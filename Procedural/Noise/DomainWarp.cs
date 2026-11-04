namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// Domain warping: offsets a sample point by fBm before it is used, bending straight noise
    /// features into organic shapes.
    /// </summary>
    /// <remarks>
    /// The x and y offsets come from two independent fBm fields (seeds <c>Mix(seed, 'X')</c> and
    /// <c>Mix(seed, 'Y')</c>), both sampled at the original point. Pure, allocation-free and thread-safe.
    /// </remarks>
    public static class DomainWarp {
        private const ulong AxisXSalt = 0x58UL;
        private const ulong AxisYSalt = 0x59UL;

        /// <summary>
        /// Moves (<paramref name="x"/>, <paramref name="y"/>) by up to <paramref name="amplitude"/>
        /// units along each axis.
        /// </summary>
        public static void Warp2(ulong seed, ref double x, ref double y, double amplitude, in FbmSettings settings) {
            double offsetX = Fbm.Sample2(StableHash.Mix(seed, AxisXSalt), x, y, settings);
            double offsetY = Fbm.Sample2(StableHash.Mix(seed, AxisYSalt), x, y, settings);
            x += amplitude * offsetX;
            y += amplitude * offsetY;
        }
    }
}
