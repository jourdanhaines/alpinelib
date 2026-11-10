namespace AlpineLib.Procedural.Meshing {
    /// <summary>One exposed stretch of a foot edge: its span from the edge start and whether it reaches the edge's ends.</summary>
    internal readonly struct TalusRun {
        public TalusRun(double from, double to, bool reachesStart, bool reachesEnd) {
            From = from;
            To = to;
            ReachesStart = reachesStart;
            ReachesEnd = reachesEnd;
        }

        public double From { get; }

        public double To { get; }

        public bool ReachesStart { get; }

        public bool ReachesEnd { get; }
    }
}
