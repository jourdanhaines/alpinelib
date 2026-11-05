using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The linear distance fog a backdrop draw sees: the share hidden at a distance, as the stylized shader's radial
    /// fog computes it (distance less the near clip, times the material's fog scale, from start to end).
    /// </summary>
    public readonly struct BackdropFog : IEquatable<BackdropFog> {
        /// <summary>Linear fog from <paramref name="start"/> to <paramref name="end"/> metres for a camera clipping at <paramref name="near"/>.</summary>
        public BackdropFog(double start, double end, double near) {
            if (!(end > start)) throw new ArgumentOutOfRangeException(nameof(end), end, "Fog must end past its start.");

            Start = start;
            End = end;
            Near = near;
            IsOn = true;
        }

        /// <summary>No fog: nothing is ever hidden.</summary>
        public static BackdropFog None => default;

        public bool IsOn { get; }
        public double Start { get; }
        public double End { get; }
        public double Near { get; }

        /// <summary>Share (0–1) of a point <paramref name="distance"/> metres away hidden by fog scaled by <paramref name="scale"/>.</summary>
        public double HiddenShare(double distance, double scale) {
            if (!IsOn) return 0.0;

            double depth = Math.Max(distance - Near, 0.0) * scale;
            return Math.Min(Math.Max((depth - Start) / (End - Start), 0.0), 1.0);
        }

        public bool Equals(BackdropFog other) {
            return IsOn == other.IsOn && Start.Equals(other.Start) && End.Equals(other.End) && Near.Equals(other.Near);
        }

        public override bool Equals(object obj) {
            return obj is BackdropFog other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(IsOn, Start, End, Near);
        }
    }
}
