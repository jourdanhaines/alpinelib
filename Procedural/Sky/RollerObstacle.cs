namespace AlpineLib.Procedural.Sky {
    /// <summary>What a wind-rolled prop's support under one point is, as far as jumping goes.</summary>
    public enum RollerObstacle {
        /// <summary>Plain ground or deck: the prop rolls on it.</summary>
        None = 0,

        /// <summary>A crest the prop pops over with a small jump (an embankment's shoulder).</summary>
        Crest = 1,

        /// <summary>An obstacle the prop jumps clear of (a rail head).</summary>
        Obstacle = 2,
    }
}
