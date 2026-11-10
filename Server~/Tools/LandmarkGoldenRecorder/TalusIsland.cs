using System.Collections.Generic;
using AllAboard.WorldGen;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.GoldenRecorder {
    /// <summary>The adapter's island talus, in the game's record types, built up one laid mass at a time.</summary>
    public sealed class TalusIsland {
        public TalusIsland(IReadOnlyList<double> rises) {
            Rises = new double[rises.Count];
            for (int mass = 0; mass < rises.Count; mass++) Rises[mass] = rises[mass];
        }

        public double[] Rises { get; }
        public List<LandmarkTalusEdge> Edges { get; } = new List<LandmarkTalusEdge>();
        public List<LandmarkBareFoot> Bare { get; } = new List<LandmarkBareFoot>();
        public List<LandmarkSlopePiece> Pieces { get; } = new List<LandmarkSlopePiece>();
        public List<IReadOnlyList<PlanPoint>> Surfaces { get; } = new List<IReadOnlyList<PlanPoint>>();

        /// <summary>Adds a laid mass's bare stretches, parts and pieces as the game commits them.</summary>
        public void Commit(int mass, TalusApronResult result) {
            foreach (TalusBareStretch stretch in result.Bare) Bare.Add(new LandmarkBareFoot(mass, stretch.Start, stretch.End));
            foreach (TalusApronPart part in result.Parts) Commit(mass, part);
        }

        private void Commit(int mass, TalusApronPart part) {
            Edges.Add(new LandmarkTalusEdge(mass, part.InnerStart, part.InnerEnd, part.OuterStart, part.OuterEnd, part.Rise, part.Width, part.IsFan, Pieces.Count, part.Pieces.Count));
            foreach (SlopedPrismPiece piece in part.Pieces) Pieces.Add(new LandmarkSlopePiece(mass, piece.Plan, piece.CentreX, piece.CentreZ, piece.TopY0, piece.SlopeX, piece.SlopeZ, piece.IsFan));
            Surfaces.Add(part.Surface);
        }
    }
}
