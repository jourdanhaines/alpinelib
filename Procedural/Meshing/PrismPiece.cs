using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Part of one band of a look during a <see cref="PrismBandDecomposer"/> run: the slab-clipped face fragments
    /// it holds, the raster cells it owns, and the prism plan and height range around them.
    /// </summary>
    internal sealed class PrismPiece {
        public PrismPiece(List<List<SlabPoint>> fragments, bool[] cells) {
            Fragments = fragments;
            Cells = cells;
            HeightRange(fragments, out double bottom, out double top);
            Bottom = Math.Floor(bottom / ConvexPlan.Grid) * ConvexPlan.Grid;
            Top = Math.Ceiling(top / ConvexPlan.Grid) * ConvexPlan.Grid;
        }

        /// <summary>Clipped faces, each a polygon of one to many points.</summary>
        public List<List<SlabPoint>> Fragments { get; }

        /// <summary>Raster cells (occupied or not) this piece is responsible for.</summary>
        public bool[] Cells { get; }

        /// <summary>The finished prism plan, set by the decomposer.</summary>
        public List<PlanPoint> Plan { get; set; }

        /// <summary>True when the plan is the fallback rectangle because cleanup could not fix the enclosed hull.</summary>
        public bool FellBack { get; set; }

        /// <summary>Lowest fragment height, rounded down to the grid.</summary>
        public double Bottom { get; }

        /// <summary>Highest fragment height, rounded up to the grid.</summary>
        public double Top { get; }

        /// <summary>Every fragment point on the plan.</summary>
        public List<PlanPoint> PlanPoints() {
            var points = new List<PlanPoint>();
            foreach (List<SlabPoint> fragment in Fragments) {
                foreach (SlabPoint point in fragment) points.Add(new PlanPoint(point.X, point.Z));
            }
            return points;
        }

        /// <summary>A piece holding both pieces' fragments and cells.</summary>
        public static PrismPiece Merge(PrismPiece first, PrismPiece second) {
            var fragments = new List<List<SlabPoint>>(first.Fragments.Count + second.Fragments.Count);
            fragments.AddRange(first.Fragments);
            fragments.AddRange(second.Fragments);
            var cells = new bool[first.Cells.Length];
            for (int index = 0; index < cells.Length; index++) cells[index] = first.Cells[index] || second.Cells[index];
            return new PrismPiece(fragments, cells);
        }

        /// <summary>The prism this piece became.</summary>
        public EnvelopePrism ToPrism() {
            double top = Top > Bottom ? Top : Bottom + ConvexPlan.Grid;
            return new EnvelopePrism(Bottom, top, Plan);
        }

        private static void HeightRange(List<List<SlabPoint>> fragments, out double bottom, out double top) {
            bottom = double.PositiveInfinity;
            top = double.NegativeInfinity;
            foreach (List<SlabPoint> fragment in fragments) {
                foreach (SlabPoint point in fragment) {
                    bottom = Math.Min(bottom, point.Y);
                    top = Math.Max(top, point.Y);
                }
            }
        }
    }
}
