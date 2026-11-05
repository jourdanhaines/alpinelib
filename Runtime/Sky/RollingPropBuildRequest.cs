using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    // One spawn to roll over a world snapshot for a life (its own, or longer for an overrun), tagged with the field's
    // generation and world version; a rebuild replaces a roll already drawn.
    internal readonly struct RollingPropBuildRequest {
        public RollingPropBuildRequest(RollerSpawn spawn, RollerCellWorld world, double life, int version, int generation, bool isRebuild) {
            Spawn = spawn;
            World = world;
            Life = life;
            Version = version;
            Generation = generation;
            IsRebuild = isRebuild;
        }

        public RollerSpawn Spawn { get; }
        public RollerCellWorld World { get; }
        public double Life { get; }
        public int Version { get; }
        public int Generation { get; }
        public bool IsRebuild { get; }
    }
}
