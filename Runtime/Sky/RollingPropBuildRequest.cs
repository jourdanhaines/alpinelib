using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    // One spawn to roll over a world snapshot, tagged with the field's generation and world version.
    internal readonly struct RollingPropBuildRequest {
        public RollingPropBuildRequest(RollerSpawn spawn, RollerCellWorld world, int version, int generation) {
            Spawn = spawn;
            World = world;
            Version = version;
            Generation = generation;
        }

        public RollerSpawn Spawn { get; }
        public RollerCellWorld World { get; }
        public int Version { get; }
        public int Generation { get; }
    }
}
