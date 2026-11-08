using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// One prism of a <see cref="PrismRegion"/> while it is being fitted: a run of layers, the face fragments it holds
    /// in each, the plan region (convex parts) whose walled-in cells it must also cover, and what its evaluation found.
    /// </summary>
    internal sealed class PrismPiece {
        public PrismPiece(int firstLayer, List<List<List<SlabPoint>>> layerFragments, List<List<PlanPoint>> region) {
            FirstLayer = firstLayer;
            LayerFragments = layerFragments;
            Region = region;
        }

        /// <summary>Index of the lowest layer the piece spans.</summary>
        public int FirstLayer { get; private set; }

        /// <summary>Index of the highest layer the piece spans.</summary>
        public int LastLayer => FirstLayer + LayerFragments.Count - 1;

        /// <summary>Fragments per spanned layer, lowest first.</summary>
        public List<List<List<SlabPoint>>> LayerFragments { get; }

        /// <summary>The plan region the piece answers for, as convex parts.</summary>
        public List<List<PlanPoint>> Region { get; }

        /// <summary>The finished prism plan.</summary>
        public List<PlanPoint> Plan { get; set; }

        /// <summary>True when the plan is the fallback rectangle because cleanup could not fix the enclosed hull.</summary>
        public bool FellBack { get; set; }

        /// <summary>Lowest height, rounded down to the grid.</summary>
        public double Bottom { get; set; }

        /// <summary>Highest height, rounded up to the grid.</summary>
        public double Top { get; set; }

        /// <summary>The bottom before a ledge fill-down lowered it; NaN when the piece was never filled.</summary>
        public double FilledFrom { get; set; } = double.NaN;

        /// <summary>Worst exposed plan standoff over the spanned layers, look metres.</summary>
        public double Standoff { get; set; }

        /// <summary>The layer, point and inward edge normal of the worst standoff sample.</summary>
        public int WorstLayer { get; set; }
        public PlanPoint WorstPoint { get; set; }
        public PlanPoint WorstInward { get; set; }

        /// <summary>The most distant over-target samples (point, inward normal, layer), deepest first, for two-notch cuts.</summary>
        public List<(PlanPoint Point, PlanPoint Inward, int Layer, double Distance)> OverSamples { get; } = new List<(PlanPoint, PlanPoint, int, double)>();

        /// <summary>Lowest layer kind among the layers over target (or of the cap), the piece's split priority.</summary>
        public int Kind { get; set; }

        /// <summary>Where a forced cut (a ledge or side the final metric found) goes through.</summary>
        public PlanPoint CapPoint { get; set; }

        /// <summary>True once no split of this piece helped; it is not tried again.</summary>
        public bool Unsplittable { get; set; }

        /// <summary>How far over target the piece is (0 or less when it meets it).</summary>
        public double Excess { get; set; }

        /// <summary>Drops spanned layers below <paramref name="first"/> and above <paramref name="last"/>.</summary>
        public void Trim(int first, int last) {
            LayerFragments.RemoveRange(last - FirstLayer + 1, LastLayer - last);
            LayerFragments.RemoveRange(0, first - FirstLayer);
            FirstLayer = first;
        }

        /// <summary>The fragments of one spanned layer.</summary>
        public List<List<SlabPoint>> Fragments(int layer) => LayerFragments[layer - FirstLayer];

        /// <summary>The prism this piece became.</summary>
        public EnvelopePrism ToPrism() {
            double top = Top > Bottom ? Top : Bottom + ConvexPlan.Grid;
            return new EnvelopePrism(Bottom, top, Plan);
        }
    }
}
