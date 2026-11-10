using System;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>Which corners <see cref="PolygonFillet.Round"/> rounds and how much of each edge a fillet may take.</summary>
    public readonly struct PolygonFilletSettings {
        /// <summary>
        /// Corners whose turn cosine is under <paramref name="sharpTurnCosine"/> are rounded; a fillet's tangent length stays
        /// within <paramref name="edgeShare"/> of either neighbouring edge (under 0.5 keeps neighbouring fillets apart).
        /// </summary>
        public PolygonFilletSettings(double sharpTurnCosine, double edgeShare) {
            if (!(sharpTurnCosine >= -1.0 && sharpTurnCosine <= 1.0)) throw new ArgumentOutOfRangeException(nameof(sharpTurnCosine), sharpTurnCosine, "Turn cosine must lie in [-1, 1].");
            if (!(edgeShare > 0.0 && edgeShare <= 0.5)) throw new ArgumentOutOfRangeException(nameof(edgeShare), edgeShare, "Edge share must lie in (0, 0.5].");

            SharpTurnCosine = sharpTurnCosine;
            EdgeShare = edgeShare;
        }

        /// <summary>Cosine of the turn (between incoming and outgoing edge directions) a corner must fall under to be rounded.</summary>
        public double SharpTurnCosine { get; }

        /// <summary>Most of either neighbouring edge a fillet's tangent length may take.</summary>
        public double EdgeShare { get; }
    }
}
