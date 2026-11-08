using System;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// How <see cref="PrismEnvelopeFitter"/> fits stacked vertical prisms to a look. Heights (step, bands,
    /// reachable top, band sizes) are world metres above <see cref="Ground"/> at <see cref="MaxScale"/> and are
    /// divided by it for the look's space; every other length is in the look's space.
    /// </summary>
    public sealed class PrismEnvelopeSettings {
        /// <summary>Ground height in the look's space; the look may sink below it.</summary>
        public double Ground { get; set; }

        /// <summary>Largest scale the look is placed at; heights below are world metres at this scale.</summary>
        public double MaxScale { get; set; } = 1.0;

        /// <summary>The pawn's step offset; the feet band tops out at <see cref="FeetTop"/>.</summary>
        public double StepOffset { get; set; } = 0.3;

        /// <summary>Gravity magnitude the pawn motor applies, m/s².</summary>
        public double Gravity { get; set; } = 20.0;

        /// <summary>The motor's fixed tick, seconds.</summary>
        public double TickSeconds { get; set; } = 1.0 / 30.0;

        /// <summary>Clearance kept under the motor's real step by the feet band cap.</summary>
        public double FeetMargin { get; set; } = 0.01;

        /// <summary>
        /// Top of the feet band: <c>StepOffset − Gravity·TickSeconds² − FeetMargin</c> (0.2678 at 30 Hz). A grounded
        /// pawn sinks one tick of gravity before the motor raises its feet by the step offset, so its real step
        /// depends on tick rate and gravity (0.278 m at 30 Hz, 0.294 m at 60 Hz).
        /// </summary>
        public double FeetTop => StepOffset - Gravity * TickSeconds * TickSeconds - FeetMargin;

        /// <summary>Most the 90th percentile of walking-band standoff may be for <see cref="PrismEnvelopeFit.MeetsTargets"/>, look metres.</summary>
        public double StandoffP90Target { get; set; } = 0.3;

        /// <summary>Most the largest walking-band standoff may be for <see cref="PrismEnvelopeFit.MeetsTargets"/>, look metres.</summary>
        public double StandoffMaxTarget { get; set; } = 0.5;

        /// <summary>When true a fit that misses a target throws instead of returning.</summary>
        public bool Strict { get; set; } = true;

        /// <summary>Bottom of the walking band whose standoff is gated.</summary>
        public double WalkingLow { get; set; } = 0.35;

        /// <summary>Top of the band the reported standoff gate samples (from <see cref="WalkingLow"/>).</summary>
        public double GateTop { get; set; } = 1.4;

        /// <summary>Top of the walking band (feet and fine bands, fitted first).</summary>
        public double WalkingTop { get; set; } = 2.25;

        /// <summary>Band edges between the feet band and <see cref="WalkingTop"/>, ascending.</summary>
        public double[] FineEdges { get; set; } = { 0.8, 1.3, 1.8 };

        /// <summary>Highest a jump reaches; the band from <see cref="WalkingTop"/> to it is fitted after the walking band.</summary>
        public double JumpReach { get; set; } = 3.5;

        /// <summary>A plateau step above the jump reach starts a new containment-only band when its flat area is at least this share of the look's plan area.</summary>
        public double TierShare { get; set; } = 0.1;

        /// <summary>Most containment-only bands above the jump reach; the smallest tiers merge away first.</summary>
        public int MaxCoarseBands { get; set; } = 4;

        /// <summary>Height of the walking layers, the finest height split, world metres at max scale.</summary>
        public double MinBand { get; set; } = 0.15;

        /// <summary>Height of the layers between <see cref="WalkingTop"/> and <see cref="JumpReach"/>, world metres at max scale.</summary>
        public double ReachLayer { get; set; } = 0.5;

        /// <summary>Share of <see cref="StandoffP90Target"/> a piece's worst exposed plan standoff may reach before it is split.</summary>
        public double PieceStandoffShare { get; set; } = 0.9;

        /// <summary>Spacing of the samples measuring a piece's exposed plan standoff, look metres.</summary>
        public double StandoffSpacing { get; set; } = 0.1;

        /// <summary>Most cap air the finished fit may leave on reachable tops for <see cref="PrismEnvelopeFit.MeetsTargets"/>, look metres.</summary>
        public double LedgeAirMax { get; set; } = 0.35;

        /// <summary>
        /// Cap strips narrower than this against a prism rising above the cap are lips no capsule stands on, look
        /// metres: a capsule centred on such a strip overlaps that prism. Defaults to the default capsule radius.
        /// </summary>
        public double LedgeLip { get; set; } = 0.35;

        /// <summary>Highest cap counted as reachable for the ledge rule.</summary>
        public double ReachableTop { get; set; } = 1.5;

        /// <summary>Most plan vertices per prism (3–16).</summary>
        public int MaxVertices { get; set; } = 16;

        /// <summary>Most prisms in a fit, the hard cap: splits stop at it. At least <see cref="MinimumBands"/>.</summary>
        public int MaxPrisms { get; set; } = 24;

        /// <summary>Footprint raster cell: walled-in cells skip the standoff measure and stop the cut march, look metres.</summary>
        public double Cell { get; set; } = 0.1;

        /// <summary>Shortest prism edge, look metres.</summary>
        public double MinEdge { get; set; } = 0.05;

        /// <summary>Smallest turn at a prism vertex, as the sine of the angle between its edges.</summary>
        public double MinTurn { get; set; } = 0.01;

        /// <summary>Spacing of the containment proof and the metrics samples, look metres.</summary>
        public double SampleSpacing { get; set; } = 0.1;

        /// <summary>Most a look sample may lie outside every prism before the fit is refused, look metres.</summary>
        public double ContainmentTolerance { get; set; } = 0.05;

        /// <summary>Fewest prisms a tall look needs: the feet prism, one over the rest of the reach, one coarse band.</summary>
        public int MinimumBands => 3;

        /// <summary>A world height above ground at max scale, in the look's space.</summary>
        public double LookHeight(double worldHeight) {
            return Ground + worldHeight / MaxScale;
        }

        /// <summary>Throws when a value would break the fit.</summary>
        public void Validate() {
            if (!(MaxScale > 0.0) || !(FeetTop > 0.0) || FeetMargin < 0.0 || Gravity < 0.0 || !(TickSeconds > 0.0)) throw new ArgumentException("Need MaxScale > 0, FeetMargin, Gravity >= 0, TickSeconds > 0 and a positive FeetTop.");
            if (FineEdges == null) throw new ArgumentException("FineEdges must not be null.");
            double previous = FeetTop;
            foreach (double edge in FineEdges) {
                if (!(edge > previous)) throw new ArgumentException("FineEdges must ascend above the feet band.");

                previous = edge;
            }
            if (!(WalkingTop > previous) || !(JumpReach > WalkingTop) || !(WalkingLow >= 0.0) || !(MinBand > 0.0) || !(ReachLayer > 0.0)) throw new ArgumentException("Need ascending bands below WalkingTop < JumpReach and positive MinBand and ReachLayer.");
            if (MaxCoarseBands < 1) throw new ArgumentException("MaxCoarseBands must be at least 1.");
            if (MaxVertices < 3 || MaxVertices > 16) throw new ArgumentException("MaxVertices must be 3–16.");
            if (MaxPrisms < MinimumBands) throw new ArgumentException("MaxPrisms must be at least " + MinimumBands + ": feet, reach and one coarse band.");
            if (!(Cell > 0.0) || !(MinEdge > 0.0) || !(MinTurn > 0.0) || !(SampleSpacing > 0.0) || !(StandoffSpacing > 0.0) || !(PieceStandoffShare > 0.0)) throw new ArgumentException("Cell, MinEdge, MinTurn, SampleSpacing, StandoffSpacing and PieceStandoffShare must be positive.");
        }
    }
}
