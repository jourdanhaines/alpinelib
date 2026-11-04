using System.Numerics;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One live smoke puff: where it is from its emitter (world axes, metres), how old it is and how it looks (shape,
    /// shade and thickness fixed at emission).
    /// </summary>
    public readonly struct Puff {
        public Puff(Vector3 offset, float age, int variant, float yaw, float spin, Vector3 drift, float effort, ulong index, in SmokeSample look) {
            Offset = offset;
            Age = age;
            Variant = variant;
            Yaw = yaw;
            Spin = spin;
            Drift = drift;
            Effort = effort;
            Index = index;
            Shade = look.Shade;
            Thickness = (float)look.Thickness;
            LifeScale = (float)look.LifeScale;
            RiseScale = (float)look.RiseScale;
        }

        private Puff(in Puff puff, Vector3 offset, float age) {
            this = puff;
            Offset = offset;
            Age = age;
        }

        /// <summary>Position from the emitter in world axes (not turned with it), metres.</summary>
        public Vector3 Offset { get; }

        /// <summary>Seconds since emission.</summary>
        public float Age { get; }

        /// <summary>Mesh shape, 0..<see cref="PuffSettings.VariantCount"/> − 1.</summary>
        public int Variant { get; }

        /// <summary>Heading at emission, radians.</summary>
        public float Yaw { get; }

        /// <summary>Turn rate about the vertical, radians per second.</summary>
        public float Spin { get; }

        /// <summary>Constant sideways wander, metres per second.</summary>
        public Vector3 Drift { get; }

        /// <summary>Effort it was emitted with, 0..1 (bigger, faster puffs).</summary>
        public float Effort { get; }

        /// <summary>Emission number on its emitter (0 for the first), the key of its randoms.</summary>
        public ulong Index { get; }

        /// <summary>Index into the style's <see cref="SmokeStyle.Shades"/>.</summary>
        public int Shade { get; }

        /// <summary>Size multiplier from its stage.</summary>
        public float Thickness { get; }

        /// <summary>Lifetime and rise multipliers from its stage.</summary>
        public float LifeScale { get; }
        public float RiseScale { get; }

        public Puff Moved(Vector3 offset, float age) {
            return new Puff(this, offset, age);
        }
    }
}
