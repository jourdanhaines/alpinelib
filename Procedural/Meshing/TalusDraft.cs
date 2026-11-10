using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>One talus part before it is kept, with what it hangs on (a run's edge and index, or a corner fan's vertex).</summary>
    internal sealed class TalusDraft {
        public TalusDraft(PlanPoint innerStart, PlanPoint innerEnd, PlanPoint outerStart, PlanPoint outerEnd, double rise, double width, bool isFan, List<SlopedPrismPiece> pieces) {
            var corners = new List<PlanPoint>();
            foreach (SlopedPrismPiece piece in pieces) corners.AddRange(piece.Plan);
            Part = new TalusApronPart(innerStart, innerEnd, outerStart, outerEnd, rise, width, isFan, pieces, ConvexPlan.Hull(corners));
        }

        public TalusApronPart Part { get; }

        /// <summary>The foot edge of the run (or of the run an end fan closes), −1 for a corner fan.</summary>
        public int RunEdge { get; private set; } = -1;

        /// <summary>The run's index on its edge, −1 for a corner fan.</summary>
        public int RunIndex { get; private set; } = -1;

        /// <summary>The corner a fan joins at, −1 otherwise.</summary>
        public int Vertex { get; private set; } = -1;

        /// <summary>True for a fan that cannot close in the rock or keep under the gradient cap.</summary>
        public bool IsOpen { get; set; }

        public TalusDraft For(int edge, int index) {
            RunEdge = edge;
            RunIndex = index;
            return this;
        }

        public TalusDraft AtVertex(int vertex) {
            Vertex = vertex;
            return this;
        }
    }
}
