using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// One record of a talus apron: a strip from the wall foot (<see cref="InnerStart"/>–<see cref="InnerEnd"/>, at
    /// <see cref="Rise"/>) down to the toe (<see cref="OuterStart"/>–<see cref="OuterEnd"/>), or a fan about one foot point
    /// (both inner points the same), with its sloped prisms and the counter-clockwise hull of their plans.
    /// </summary>
    public sealed class TalusApronPart {
        /// <summary>Builds a part.</summary>
        internal TalusApronPart(PlanPoint innerStart, PlanPoint innerEnd, PlanPoint outerStart, PlanPoint outerEnd, double rise, double width, bool isFan,
                              IReadOnlyList<SlopedPrismPiece> pieces, IReadOnlyList<PlanPoint> surface) {
            InnerStart = innerStart;
            InnerEnd = innerEnd;
            OuterStart = outerStart;
            OuterEnd = outerEnd;
            Rise = rise;
            Width = width;
            IsFan = isFan;
            Pieces = pieces;
            Surface = surface;
        }

        /// <summary>Wall foot start (the surface meets the wall here at <see cref="Rise"/>).</summary>
        public PlanPoint InnerStart { get; }

        /// <summary>Wall foot end.</summary>
        public PlanPoint InnerEnd { get; }

        /// <summary>Toe start (the surface meets the ground near here).</summary>
        public PlanPoint OuterStart { get; }

        /// <summary>Toe end.</summary>
        public PlanPoint OuterEnd { get; }

        /// <summary>Surface height at the wall foot, rounded to <see cref="ConvexPlan.Grid"/>.</summary>
        public double Rise { get; }

        /// <summary>Plan width from the wall foot to the toe, rounded to <see cref="ConvexPlan.Grid"/>.</summary>
        public double Width { get; }

        /// <summary>True for a fan (a corner fan joining two strips or an end fan closing one).</summary>
        public bool IsFan { get; }

        /// <summary>The part's sloped prisms.</summary>
        public IReadOnlyList<SlopedPrismPiece> Pieces { get; }

        /// <summary>The counter-clockwise hull of the pieces' plans.</summary>
        public IReadOnlyList<PlanPoint> Surface { get; }
    }
}
