namespace AlpineLib.Procedural.Sky {
    /// <summary>Why a wind-rolled prop's roll ends (it fades out over its last metres either way).</summary>
    public enum RollerEnd {
        /// <summary>It rolled its seeded life and broke up.</summary>
        Life = 0,

        /// <summary>It met something too big to hop.</summary>
        Blocked = 1,

        /// <summary>It reached the edge of the loaded world; a wider world may carry it on.</summary>
        Edge = 2,
    }
}
