using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// One thin slab of a <see cref="PrismRegion"/>: the look's faces clipped to it and their footprint on the region's
    /// shared frame. Pieces span runs of layers; a height split always falls on a layer edge.
    /// </summary>
    internal sealed class PrismLayer {
        /// <summary>Layers whose sides the standoff gate samples or whose caps the ledge gate can reach; split first.</summary>
        public const int Gate = 0;

        /// <summary>Other layers of the walking band.</summary>
        public const int Walking = 1;

        /// <summary>The feet layer: sides under the gate, cap a step.</summary>
        public const int Feet = 2;

        /// <summary>Layers between the walking band and the jump reach.</summary>
        public const int Reach = 3;

        public PrismLayer(double bottom, double top, int kind, List<List<SlabPoint>> fragments, PlanFrame frame) {
            Bottom = bottom;
            Top = top;
            Kind = kind;
            Fragments = fragments;
            Footprint = new PlanFootprint(fragments, top, frame);
        }

        public double Bottom { get; }
        public double Top { get; }
        public int Kind { get; }
        public List<List<SlabPoint>> Fragments { get; }
        public PlanFootprint Footprint { get; }
    }
}
