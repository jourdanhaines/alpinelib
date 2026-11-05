using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// How <see cref="BoxEnvelopeFitter"/> slices and merges: the heights the first boxes split at, the most
    /// boxes kept, the yaw search step and points no box may swallow (the air just above a walkable top).
    /// </summary>
    public sealed class BoxEnvelopeSettings {
        /// <summary>Heights the starting boxes split at, ascending; geometry above and below the ends forms its own band.</summary>
        public IReadOnlyList<double> BandEdges { get; set; } = Array.Empty<double>();

        /// <summary>Most boxes in the result.</summary>
        public int MaxBoxes { get; set; } = 1;

        /// <summary>Step of the per-piece yaw search, whole degrees (1–90).</summary>
        public int YawStepDegrees { get; set; } = 1;

        /// <summary>
        /// Points no merged box may hold deeper than <see cref="KeepOutTolerance"/>; where a starting box already holds
        /// one, merges may not raise a box over it any higher than that box's top.
        /// </summary>
        public IReadOnlyList<Vector3> KeepOut { get; set; } = Array.Empty<Vector3>();

        /// <summary>How deep inside a box a keep-out point may sit before the merge is refused.</summary>
        public double KeepOutTolerance { get; set; } = 0.01;

        /// <summary>Height under which added volume costs <see cref="WeightBelow"/> times as much (the walking band, where solid air shows).</summary>
        public double WeightedBelow { get; set; } = double.NegativeInfinity;

        /// <summary>Cost factor of added volume below <see cref="WeightedBelow"/> (at least 1).</summary>
        public double WeightBelow { get; set; } = 1.0;

        /// <summary>Grid the result's centres and half-extents round to; half-extents round outward so containment survives.</summary>
        public double Snap { get; set; } = 0.001;

        /// <summary>Throws when a value would break the fit.</summary>
        public void Validate() {
            if (MaxBoxes < 1) throw new ArgumentException("MaxBoxes must be at least 1.");
            if (YawStepDegrees < 1 || YawStepDegrees > 90) throw new ArgumentException("YawStepDegrees must be 1–90.");
            if (WeightBelow < 1.0) throw new ArgumentException("WeightBelow must be at least 1.");
            if (KeepOutTolerance < 0.0 || Snap <= 0.0) throw new ArgumentException("KeepOutTolerance must not be negative and Snap must be positive.");
            if (BandEdges == null || KeepOut == null) throw new ArgumentException("BandEdges and KeepOut must not be null.");
            for (int index = 1; index < BandEdges.Count; index++) {
                if (BandEdges[index] <= BandEdges[index - 1]) throw new ArgumentException("BandEdges must ascend.");
            }
        }
    }
}
