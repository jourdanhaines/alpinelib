using System.Collections.Generic;
using AllAboard.WorldGen;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.GoldenRecorder {
    /// <summary>The arguments of one game talus build.</summary>
    public sealed class TalusInputs {
        public TalusInputs(IReadOnlyList<IReadOnlyList<PlanPoint>> feet, IReadOnlyList<LandmarkMass> masses, IReadOnlyList<double> rises, double slope, IReadOnlyList<bool[]> bare,
                           IReadOnlyList<LandmarkCleft> clefts, IReadOnlyList<LandmarkAlcove> alcoves, LandmarkTunnel tunnel, LandmarkSiteContext context,
                           IReadOnlyList<IReadOnlyList<PlanPoint>> neighbours, IReadOnlyList<LandmarkTalusTop> tops) {
            Feet = feet;
            Masses = masses;
            Rises = rises;
            Slope = slope;
            Bare = bare;
            Clefts = clefts;
            Alcoves = alcoves;
            Tunnel = tunnel;
            Context = context;
            Neighbours = neighbours;
            Tops = tops;
        }

        public IReadOnlyList<IReadOnlyList<PlanPoint>> Feet { get; }
        public IReadOnlyList<LandmarkMass> Masses { get; }
        public IReadOnlyList<double> Rises { get; }
        public double Slope { get; }
        public IReadOnlyList<bool[]> Bare { get; }
        public IReadOnlyList<LandmarkCleft> Clefts { get; }
        public IReadOnlyList<LandmarkAlcove> Alcoves { get; }
        public LandmarkTunnel Tunnel { get; }
        public LandmarkSiteContext Context { get; }
        public IReadOnlyList<IReadOnlyList<PlanPoint>> Neighbours { get; }
        public IReadOnlyList<LandmarkTalusTop> Tops { get; }
    }
}
