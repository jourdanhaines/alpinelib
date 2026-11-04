using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One <see cref="CloudClass"/>: the diameter and altitude range its clouds take, how many puffs its meshes are
    /// heaped from, how often it is picked and the triangle budget of one mesh.
    /// </summary>
    public sealed class CloudClassSettings {
        public CloudClassSettings(double minDiameter, double maxDiameter, double minAltitude, double maxAltitude, int minPuffs, int maxPuffs, double weight, int faceBudget) {
            MinDiameter = minDiameter;
            MaxDiameter = maxDiameter;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            MinPuffs = minPuffs;
            MaxPuffs = maxPuffs;
            Weight = weight;
            FaceBudget = faceBudget;
        }

        /// <summary>Footprint diameter range of a drawn cloud, metres (mesh diameter × instance scale).</summary>
        public double MinDiameter { get; }
        public double MaxDiameter { get; }

        /// <summary>Altitude range of a cloud's flat base above the ground, metres.</summary>
        public double MinAltitude { get; }
        public double MaxAltitude { get; }

        /// <summary>Puffs one mesh of the class is heaped from.</summary>
        public int MinPuffs { get; }
        public int MaxPuffs { get; }

        /// <summary>Relative chance a cloud is of this class.</summary>
        public double Weight { get; }

        /// <summary>Most triangles one mesh of the class may have.</summary>
        public int FaceBudget { get; }

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (!(MinDiameter > 0.0 && MaxDiameter >= MinDiameter)) throw new ArgumentOutOfRangeException(nameof(MaxDiameter), MaxDiameter, "Need 0 < min <= max diameter.");
            if (!(MinAltitude >= 0.0 && MaxAltitude >= MinAltitude)) throw new ArgumentOutOfRangeException(nameof(MaxAltitude), MaxAltitude, "Need 0 <= min <= max altitude.");
            if (MinPuffs < 1 || MaxPuffs < MinPuffs) throw new ArgumentOutOfRangeException(nameof(MaxPuffs), MaxPuffs, "Need 1 <= min <= max puffs.");
            if (!(Weight >= 0.0)) throw new ArgumentOutOfRangeException(nameof(Weight), Weight, "Must not be negative.");
            if (FaceBudget < MaxPuffs * 20) throw new ArgumentOutOfRangeException(nameof(FaceBudget), FaceBudget, "Too small for one coarse blob per puff.");
        }
    }
}
