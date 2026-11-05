namespace AlpineLib.Sky {
    // A finished build: the roll (null when no spawn point was clear) and the request's tags.
    internal readonly struct RollingPropBuildResult {
        public RollingPropBuildResult(ulong id, RollingProp? prop, int version, int generation, bool isRebuild) {
            Id = id;
            Prop = prop;
            Version = version;
            Generation = generation;
            IsRebuild = isRebuild;
        }

        public ulong Id { get; }
        public RollingProp? Prop { get; }
        public int Version { get; }
        public int Generation { get; }
        public bool IsRebuild { get; }
    }
}
