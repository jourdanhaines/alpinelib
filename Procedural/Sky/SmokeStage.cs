using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One look of a <see cref="SmokeStyle"/>: pure at <see cref="Efficiency"/>, cross-faded with its neighbours in
    /// between. Shades are drawn by weight; thickness, life and rise scale every puff emitted in it.
    /// </summary>
    public sealed class SmokeStage {
        public SmokeStage(double efficiency, IReadOnlyList<SmokeShadeWeight> shades, double thickness = 1.0, double lifeScale = 1.0, double riseScale = 1.0) {
            Efficiency = efficiency;
            Shades = shades ?? throw new ArgumentNullException(nameof(shades));
            Thickness = thickness;
            LifeScale = lifeScale;
            RiseScale = riseScale;
        }

        /// <summary>Efficiency (0..1) at which this stage is the only one drawn from.</summary>
        public double Efficiency { get; }

        public IReadOnlyList<SmokeShadeWeight> Shades { get; }
        public double Thickness { get; }
        public double LifeScale { get; }
        public double RiseScale { get; }

        /// <summary>Sum of the shade weights.</summary>
        public double TotalWeight {
            get {
                double total = 0.0;
                foreach (SmokeShadeWeight shade in Shades) total += shade.Weight;
                return total;
            }
        }

        /// <summary>Probability of <paramref name="shade"/> within this stage alone.</summary>
        public double ShareOf(int shade) {
            double total = TotalWeight;
            double weight = 0.0;
            foreach (SmokeShadeWeight entry in Shades) {
                if (entry.Shade == shade) weight += entry.Weight;
            }

            return total > 0.0 ? weight / total : 0.0;
        }

        /// <summary>The shade at <paramref name="roll"/> (0..1) through the weights.</summary>
        public int Pick(double roll) {
            double target = Math.Min(1.0, Math.Max(0.0, roll)) * TotalWeight;
            double sum = 0.0;
            foreach (SmokeShadeWeight entry in Shades) {
                sum += entry.Weight;
                if (target < sum) return entry.Shade;
            }

            return Shades[Shades.Count - 1].Shade;
        }
    }
}
