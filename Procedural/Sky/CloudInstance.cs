using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One seeded cloud of a field cell of its class's lattice: where it is anchored in that wind frame, how high,
    /// which mesh, its size and heading, and the seed of its wander and surge. Pure data, the same for every client of a seed.
    /// </summary>
    public readonly struct CloudInstance {
        public CloudInstance(double anchorX, double anchorZ, double altitude, CloudClass cloudClass, int variant, double scale, double reach, double yaw, ulong wanderSeed, ulong seed) {
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            Altitude = altitude;
            Class = cloudClass;
            Variant = variant;
            Scale = scale;
            Reach = reach;
            Yaw = yaw;
            YawCos = Math.Cos(yaw);
            YawSin = Math.Sin(yaw);
            WanderSeed = wanderSeed;
            Seed = seed;
        }

        /// <summary>Anchor within its field cell, metres from the cell's minimum corner in its class's wind frame.</summary>
        public double AnchorX { get; }
        public double AnchorZ { get; }

        /// <summary>Height of the cloud's flat base above the ground, metres.</summary>
        public double Altitude { get; }

        public CloudClass Class { get; }

        /// <summary>Mesh variant within its class.</summary>
        public int Variant { get; }

        /// <summary>Uniform scale on the mesh (never stretched, so its puffs stay round).</summary>
        public double Scale { get; }

        /// <summary>Horizontal radius the drawn cloud stays within about its centre, metres (<see cref="CloudFieldSettings.ReachOf"/>).</summary>
        public double Reach { get; }

        /// <summary>Heading of the mesh, radians.</summary>
        public double Yaw { get; }

        /// <summary>Cosine and sine of <see cref="Yaw"/>, for building the cloud's matrix without trig per frame.</summary>
        public double YawCos { get; }
        public double YawSin { get; }

        /// <summary>Seed of the bounded wander and surge about the anchor (<see cref="CloudFieldScatter.Offset"/>).</summary>
        public ulong WanderSeed { get; }

        /// <summary>The cloud's own seed, for anything else that wants to vary per cloud.</summary>
        public ulong Seed { get; }

        /// <summary>Index of its mesh among <see cref="CloudFieldSettings.MeshCount"/>: class-major.</summary>
        public int MeshIndex => (int)Class * CloudFieldSettings.VariantsPerClass + Variant;
    }
}
