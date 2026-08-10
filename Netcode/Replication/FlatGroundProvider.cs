namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// A ground plane at a fixed height. The default provider everywhere the real surface is not
    /// available: the dedicated server in v1, and every test that cares about the motor rather than the
    /// terrain.
    /// </summary>
    public sealed class FlatGroundProvider : IGroundProvider {
        private readonly float height;

        /// <summary>Creates a plane at y = 0.</summary>
        public FlatGroundProvider() : this(0f) { }

        /// <summary>Creates a plane at the given height.</summary>
        public FlatGroundProvider(float height) {
            this.height = height;
        }

        /// <summary>The plane's height, which every sample returns.</summary>
        public float Height => height;

        /// <inheritdoc />
        public float SampleHeight(float x, float z) {
            return height;
        }
    }
}
