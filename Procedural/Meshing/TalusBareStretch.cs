namespace AlpineLib.Procedural.Meshing {
    /// <summary>A stretch of a talus foot left bare (rock straight to the ground).</summary>
    public readonly struct TalusBareStretch {
        /// <summary>Builds a stretch.</summary>
        internal TalusBareStretch(PlanPoint start, PlanPoint end) {
            Start = start;
            End = end;
        }

        /// <summary>Where the stretch starts on the foot.</summary>
        public PlanPoint Start { get; }

        /// <summary>Where the stretch ends on the foot.</summary>
        public PlanPoint End { get; }
    }
}
