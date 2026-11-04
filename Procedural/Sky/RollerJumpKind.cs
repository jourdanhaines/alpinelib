namespace AlpineLib.Procedural.Sky {
    /// <summary>Why a wind-rolled prop leaves the ground.</summary>
    public enum RollerJumpKind {
        /// <summary>Clearing an obstacle run of its support (a rail head).</summary>
        Obstacle = 0,

        /// <summary>A small pop over a crest run (an embankment's shoulder).</summary>
        Crest = 1,

        /// <summary>A seeded bump on open ground, as if it hit a stone.</summary>
        Bump = 2,

        /// <summary>The small second bounce after a landing.</summary>
        Bounce = 3,
    }
}
