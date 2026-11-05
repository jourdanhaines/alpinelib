using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>The boxes of a <see cref="WalkingBandFitter"/> run, by the part of the look they hold.</summary>
    public sealed class WalkingBandFit {
        /// <summary>Full-height columns around walls, ground to the band top.</summary>
        public List<EnvelopeBox> Columns { get; } = new List<EnvelopeBox>();

        /// <summary>Boxes no taller than the step: aprons and feet.</summary>
        public List<EnvelopeBox> Feet { get; } = new List<EnvelopeBox>();

        /// <summary>Flat steps over low lumps between the step and the band top.</summary>
        public List<EnvelopeBox> Steps { get; } = new List<EnvelopeBox>();

        /// <summary>Coarse boxes above the band top.</summary>
        public List<EnvelopeBox> Upper { get; } = new List<EnvelopeBox>();

        /// <summary>Cells too steep for a flat step within the ledge tolerance, boxed one by one anyway.</summary>
        public int SteepCells { get; set; }

        /// <summary>Every box, columns first.</summary>
        public List<EnvelopeBox> All() {
            var all = new List<EnvelopeBox>(Columns.Count + Feet.Count + Steps.Count + Upper.Count);
            all.AddRange(Columns);
            all.AddRange(Feet);
            all.AddRange(Steps);
            all.AddRange(Upper);
            return all;
        }
    }
}
