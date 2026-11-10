using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    /// <summary>
    /// One recorded apron lay round a real landmark foot: its inputs, the bare flag per foot edge, the keys of every piece
    /// the game's clear test refused, and the hash of the result (bit-identical to the game's talus builder).
    /// </summary>
    public sealed class TalusCase {
        public TalusCase(double rise, double slope, string bareEdges, List<PlanPoint> foot, List<IReadOnlyList<PlanPoint>> others, List<IReadOnlyList<PlanPoint>> keepOffs,
                         ulong[] refused, ulong resultHash, int partCount) {
            Rise = rise;
            Slope = slope;
            BareEdges = bareEdges;
            Foot = foot;
            Others = others;
            KeepOffs = keepOffs;
            Refused = new HashSet<ulong>(refused);
            ResultHash = resultHash;
            PartCount = partCount;
        }

        public double Rise { get; }

        public double Slope { get; }

        /// <summary>'1' per bare foot edge.</summary>
        public string BareEdges { get; }

        public List<PlanPoint> Foot { get; }

        public List<IReadOnlyList<PlanPoint>> Others { get; }

        public List<IReadOnlyList<PlanPoint>> KeepOffs { get; }

        public HashSet<ulong> Refused { get; }

        public ulong ResultHash { get; }

        public int PartCount { get; }
    }
}
