using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One <see cref="CloudClass"/>: the mesh diameters and altitude band its clouds take, how many puffs its meshes are
    /// heaped from, how many candidates each field cell draws of it, the triangle budget of one mesh, and how it moves:
    /// the share of the wind's drift its own lattice slides by, its bounded wander and its bounded along-wind surge.
    /// </summary>
    public sealed class CloudClassSettings {
        public CloudClassSettings(double minDiameter, double maxDiameter, double minAltitude, double maxAltitude, int minPuffs, int maxPuffs, int candidatesPerCell, int faceBudget, double heightShare, double driftFactor, double wanderAmplitude, double surgeAmplitude) {
            MinDiameter = minDiameter;
            MaxDiameter = maxDiameter;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            MinPuffs = minPuffs;
            MaxPuffs = maxPuffs;
            CandidatesPerCell = candidatesPerCell;
            FaceBudget = faceBudget;
            HeightShare = heightShare;
            DriftFactor = driftFactor;
            WanderAmplitude = wanderAmplitude;
            SurgeAmplitude = surgeAmplitude;
        }

        /// <summary>Footprint diameter range of the class's meshes, metres; each instance scales its mesh on top.</summary>
        public double MinDiameter { get; }
        public double MaxDiameter { get; }

        /// <summary>Altitude range of a cloud's flat base above the ground, metres.</summary>
        public double MinAltitude { get; }
        public double MaxAltitude { get; }

        /// <summary>Puffs one mesh of the class is heaped from.</summary>
        public int MinPuffs { get; }
        public int MaxPuffs { get; }

        /// <summary>Candidates of this class each field cell draws; crowded and out-of-coverage ones are dropped.</summary>
        public int CandidatesPerCell { get; }

        /// <summary>Most triangles one mesh of the class may have.</summary>
        public int FaceBudget { get; }

        /// <summary>Bound on a mesh's height over its diameter (fewer puffs heap taller): what class altitude bands are kept apart by.</summary>
        public double HeightShare { get; }

        /// <summary>Share of the wind's drift the class's lattice slides by: big clouds below 1 lag the air, small ones above lead it.</summary>
        public double DriftFactor { get; }

        /// <summary>Most a cloud strays from its anchor in any direction, metres.</summary>
        public double WanderAmplitude { get; }

        /// <summary>Most a cloud runs ahead of or behind its lattice along the wind, metres: its own speed variance.</summary>
        public double SurgeAmplitude { get; }

        /// <summary>Most a cloud's centre ever sits from its anchor, metres (wander plus surge).</summary>
        public double MotionReach => WanderAmplitude + SurgeAmplitude;

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (!(MinDiameter > 0.0 && MaxDiameter >= MinDiameter)) throw new ArgumentOutOfRangeException(nameof(MaxDiameter), MaxDiameter, "Need 0 < min <= max diameter.");
            if (!(MinAltitude >= 0.0 && MaxAltitude >= MinAltitude)) throw new ArgumentOutOfRangeException(nameof(MaxAltitude), MaxAltitude, "Need 0 <= min <= max altitude.");
            if (MinPuffs < 1 || MaxPuffs < MinPuffs) throw new ArgumentOutOfRangeException(nameof(MaxPuffs), MaxPuffs, "Need 1 <= min <= max puffs.");
            if (CandidatesPerCell < 0) throw new ArgumentOutOfRangeException(nameof(CandidatesPerCell), CandidatesPerCell, "Must not be negative.");
            if (FaceBudget < MaxPuffs * 20) throw new ArgumentOutOfRangeException(nameof(FaceBudget), FaceBudget, "Too small for one coarse blob per puff.");
            if (!(HeightShare > 0.0)) throw new ArgumentOutOfRangeException(nameof(HeightShare), HeightShare, "Must be positive.");
            if (!(DriftFactor > 0.0)) throw new ArgumentOutOfRangeException(nameof(DriftFactor), DriftFactor, "Must be positive.");
            if (!(WanderAmplitude >= 0.0)) throw new ArgumentOutOfRangeException(nameof(WanderAmplitude), WanderAmplitude, "Must not be negative.");
            if (!(SurgeAmplitude >= 0.0)) throw new ArgumentOutOfRangeException(nameof(SurgeAmplitude), SurgeAmplitude, "Must not be negative.");
        }
    }
}
