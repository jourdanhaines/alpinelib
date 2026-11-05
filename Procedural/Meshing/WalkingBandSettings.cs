using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// How <see cref="WalkingBandFitter"/> fits a look a walking capsule meets: the ground, the step a capsule
    /// walks over, the top of the band it can touch, the plan grid and how far a box may stand off the look.
    /// </summary>
    public sealed class WalkingBandSettings {
        /// <summary>Ground height in the look's space.</summary>
        public double Ground { get; set; }

        /// <summary>Highest top a capsule steps onto without noticing (ground plus the step offset).</summary>
        public double Step { get; set; } = 0.3;

        /// <summary>Top of the walking band; pieces reaching it are walls, boxed in full-height columns up to it.</summary>
        public double Top { get; set; } = 2.25;

        /// <summary>
        /// Pieces whose highest point reaches this height are walls, boxed in full-height columns up to <see cref="Top"/>
        /// (no reachable top over a steep face); lower pieces are lumps under flat steps. Defaults to <see cref="Top"/>.
        /// </summary>
        public double WallHeight { get; set; } = double.NaN;

        /// <summary>How far past the look a box no taller than the step may reach, metres; defaults to <see cref="Reach"/>.</summary>
        public double FootReach { get; set; } = double.NaN;

        /// <summary>Plan grid cell, metres; boxes stand off the look by about a cell plus <see cref="Reach"/>.</summary>
        public double Cell { get; set; } = 0.2;

        /// <summary>How far past the look a column or step box may reach, metres (solid air the player meets).</summary>
        public double Reach { get; set; } = 0.3;

        /// <summary>Most air a box top below <see cref="Top"/> may leave over the look or ground under it, metres.</summary>
        public double LedgeTolerance { get; set; } = 0.3;

        /// <summary>Yaw step between the turned grids rectangles are drawn from, whole degrees (1–45).</summary>
        public int YawStepDegrees { get; set; } = 5;

        /// <summary>Heights the boxes above <see cref="Top"/> split at, ascending.</summary>
        public IReadOnlyList<double> UpperBandEdges { get; set; } = Array.Empty<double>();

        /// <summary>Most boxes above <see cref="Top"/>.</summary>
        public int UpperMaxBoxes { get; set; } = 1;

        /// <summary>Grid the result's centres and half-extents round to (outward).</summary>
        public double Snap { get; set; } = 0.001;

        /// <summary>Throws when a value would break the fit.</summary>
        public void Validate() {
            if (!(Step >= Ground && Top > Step)) throw new ArgumentException("Need Ground <= Step < Top.");
            if (!(Cell > 0.0) || Reach < 0.0 || LedgeTolerance < 0.0 || !(Snap > 0.0)) throw new ArgumentException("Cell and Snap must be positive, Reach and LedgeTolerance not negative.");
            if (YawStepDegrees < 1 || YawStepDegrees > 45) throw new ArgumentException("YawStepDegrees must be 1–45.");
            if (UpperMaxBoxes < 1 || UpperBandEdges == null) throw new ArgumentException("UpperMaxBoxes must be at least 1 and UpperBandEdges not null.");
        }
    }
}
