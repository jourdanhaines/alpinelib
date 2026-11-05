namespace AlpineLib.Procedural.Sky {
    /// <summary>Whether a wind-rolled prop's ball may be at a point of the caller's world.</summary>
    public enum RollerBlock {
        /// <summary>Clear: the prop rolls on (low props it hops are part of the support, not blocks).</summary>
        None = 0,

        /// <summary>Something too big to hop (a boulder, a building): the prop breaks up against it.</summary>
        Stop = 1,

        /// <summary>Outside the loaded world: nothing is known there, so the roll ends at the edge.</summary>
        Edge = 2,
    }
}
