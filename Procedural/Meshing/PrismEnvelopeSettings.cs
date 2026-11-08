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

        /// <summary>The pawn's step offset; the feet band tops out <see cref="FeetMargin"/> below it.</summary>
        public double StepOffset { get; set; } = 0.3;

        /// <summary>How far below <see cref="StepOffset"/> the feet band ends.</summary>
        public double FeetMargin { get; set; } = 0.01;

        /// <summary>Bottom of the walking band whose standoff is gated.</summary>
        public double WalkingLow { get; set; } = 0.35;

        /// <summary>Top of the band the reported standoff gate samples (from <see cref="WalkingLow"/>).</summary>
        public double GateTop { get; set; } = 1.4;

        /// <summary>Top of the walking band; above it bands are coarse.</summary>
        public double WalkingTop { get; set; } = 2.25;

        /// <summary>Band edges between the feet band and <see cref="WalkingTop"/>, ascending.</summary>
        public double[] FineEdges { get; set; } = { 0.8, 1.3, 1.8 };

        /// <summary>Tallest coarse band above <see cref="WalkingTop"/>.</summary>
        public double CoarseBand { get; set; } = 3.0;

        /// <summary>Shortest band halving may leave.</summary>
        public double MinBand { get; set; } = 0.15;

        /// <summary>Most plan growth a band may show between its lower and upper half from lean, look metres.</summary>
        public double LeanError { get; set; } = 0.1;

        /// <summary>A band halves while the 90th percentile of its side standoff exceeds this, look metres.</summary>
        public double StandoffTarget { get; set; } = 0.25;

        /// <summary>A reachable band halves while a cap of it leaves more air than this, look metres.</summary>
        public double LedgeAirTarget { get; set; } = 0.3;

        /// <summary>Most cap air the finished fit should leave on reachable tops, look metres (reported).</summary>
        public double LedgeAirMax { get; set; } = 0.35;

        /// <summary>Cap strips narrower than this beside the next prism are lips no capsule stands on, look metres.</summary>
        public double LedgeLip { get; set; } = 0.1;

        /// <summary>Highest cap counted as reachable for the ledge rule.</summary>
        public double ReachableTop { get; set; } = 1.5;

        /// <summary>Deepest notch a hull may span before its piece is cut, look metres.</summary>
        public double NotchTolerance { get; set; } = 0.25;

        /// <summary>Most plan vertices per prism (3–8).</summary>
        public int MaxVertices { get; set; } = 8;

        /// <summary>Most prisms in a fit; cuts and halvings stop at it.</summary>
        public int MaxPrisms { get; set; } = 24;

        /// <summary>Plan raster cell guiding the cuts, look metres.</summary>
        public double Cell { get; set; } = 0.25;

        /// <summary>Shortest prism edge, look metres.</summary>
        public double MinEdge { get; set; } = 0.05;

        /// <summary>Smallest turn at a prism vertex, as the sine of the angle between its edges.</summary>
        public double MinTurn { get; set; } = 0.01;

        /// <summary>Spacing of the containment proof and the metrics samples, look metres.</summary>
        public double SampleSpacing { get; set; } = 0.1;

        /// <summary>Most a look sample may lie outside every prism before the fit is refused, look metres.</summary>
        public double ContainmentTolerance { get; set; } = 0.05;

        /// <summary>A world height above ground at max scale, in the look's space.</summary>
        public double LookHeight(double worldHeight) {
            return Ground + worldHeight / MaxScale;
        }

        /// <summary>Throws when a value would break the fit.</summary>
        public void Validate() {
            if (!(MaxScale > 0.0) || !(StepOffset > FeetMargin) || FeetMargin < 0.0) throw new ArgumentException("Need MaxScale > 0 and StepOffset > FeetMargin >= 0.");
            if (FineEdges == null) throw new ArgumentException("FineEdges must not be null.");
            double previous = StepOffset - FeetMargin;
            foreach (double edge in FineEdges) {
                if (!(edge > previous)) throw new ArgumentException("FineEdges must ascend above the feet band.");

                previous = edge;
            }
            if (!(WalkingTop > previous) || !(WalkingLow >= 0.0) || !(CoarseBand > 0.0) || !(MinBand > 0.0)) throw new ArgumentException("Need ascending bands below WalkingTop and positive CoarseBand and MinBand.");
            if (MaxVertices < 3 || MaxVertices > 8 || MaxPrisms < 1) throw new ArgumentException("MaxVertices must be 3–8 and MaxPrisms at least 1.");
            if (!(Cell > 0.0) || !(MinEdge > 0.0) || !(MinTurn > 0.0) || !(SampleSpacing > 0.0) || !(NotchTolerance > 0.0)) throw new ArgumentException("Cell, MinEdge, MinTurn, SampleSpacing and NotchTolerance must be positive.");
        }
    }
}
