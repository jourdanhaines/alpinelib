using System.Collections.Generic;
using AllAboard.GoldenRecorder;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.WorldGen {
    /// <summary>Facade over the game's talus builder: returns the game's island, checking it against the lib adapter's.</summary>
    public sealed class LandmarkTalus {
        public const double SlopeMin = LandmarkTalusGame.SlopeMin, SlopeMax = LandmarkTalusGame.SlopeMax, GradientMax = LandmarkTalusGame.GradientMax;

        private readonly LandmarkTalusGame _game;

        private LandmarkTalus(LandmarkTalusGame game) {
            _game = game;
        }

        public IReadOnlyList<double> Rises => _game.Rises;

        public double Slope => _game.Slope;

        public IReadOnlyList<LandmarkTalusEdge> Edges => _game.Edges;

        public IReadOnlyList<LandmarkBareFoot> Bare => _game.Bare;

        public IReadOnlyList<LandmarkSlopePiece> Pieces => _game.Pieces;

        public IReadOnlyList<IReadOnlyList<PlanPoint>> Surfaces => _game.Surfaces;

        public static List<bool[]> DrawBare(List<LandmarkMassDraft> drafts, SplitMix64 random) => LandmarkTalusGame.DrawBare(drafts, random);

        public static double DrawSlope(SplitMix64 random) => LandmarkTalusGame.DrawSlope(random);

        public static LandmarkTalus Build(IReadOnlyList<IReadOnlyList<PlanPoint>> feet, IReadOnlyList<LandmarkMass> masses, IReadOnlyList<double> rises, double slope,
                                          IReadOnlyList<bool[]> bare, IReadOnlyList<LandmarkCleft> clefts, IReadOnlyList<LandmarkAlcove> alcoves, LandmarkTunnel tunnel,
                                          LandmarkSiteContext context, IReadOnlyList<IReadOnlyList<PlanPoint>> neighbours, IReadOnlyList<LandmarkTalusTop> tops) {
            // The game builder changes none of its inputs: run it first, then the lib adapter on the same inputs.
            LandmarkTalusGame game = LandmarkTalusGame.Build(feet, masses, rises, slope, bare, clefts, alcoves, tunnel, context, neighbours, tops);
            TalusRecorder.Check(game, new TalusInputs(feet, masses, rises, slope, bare, clefts, alcoves, tunnel, context, neighbours, tops));
            return new LandmarkTalus(game);
        }
    }
}
