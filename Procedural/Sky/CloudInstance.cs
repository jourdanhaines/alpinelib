namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One seeded cloud of a field cell: where it is anchored in the wind frame, how high, which mesh, its size and
    /// heading, and the seed of its wander. Pure data, the same for every client of a seed.
    /// </summary>
    public readonly struct CloudInstance {
        public CloudInstance(double anchorX, double anchorZ, double altitude, CloudClass cloudClass, int variant, double scale, double yaw, ulong wanderSeed, ulong seed) {
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            Altitude = altitude;
            Class = cloudClass;
            Variant = variant;
            Scale = scale;
            Yaw = yaw;
            WanderSeed = wanderSeed;
            Seed = seed;
        }

        /// <summary>Anchor within its field cell, metres from the cell's minimum corner in the wind frame.</summary>
        public double AnchorX { get; }
        public double AnchorZ { get; }

        /// <summary>Height of the cloud's flat base above the ground, metres.</summary>
        public double Altitude { get; }

        public CloudClass Class { get; }

        /// <summary>Mesh variant within its class.</summary>
        public int Variant { get; }

        /// <summary>Uniform scale on the mesh (never stretched, so its puffs stay round).</summary>
        public double Scale { get; }

        /// <summary>Heading of the mesh, radians.</summary>
        public double Yaw { get; }

        /// <summary>Seed of the bounded wander about the anchor (<see cref="CloudFieldScatter.Wander"/>).</summary>
        public ulong WanderSeed { get; }

        /// <summary>The cloud's own seed, for anything else that wants to vary per cloud.</summary>
        public ulong Seed { get; }

        /// <summary>Index of its mesh among <see cref="CloudFieldSettings.MeshCount"/>: class-major.</summary>
        public int MeshIndex => (int)Class * CloudFieldSettings.VariantsPerClass + Variant;
    }
}
