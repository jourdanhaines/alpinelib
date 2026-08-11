namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// What a mover does when it reaches the end of its waypoint list. The numeric values are written
    /// into every exported <c>.geo</c> file, so they are a wire contract.
    /// </summary>
    public enum MoverLoopMode : byte {
        /// <summary>Teleports back to the first waypoint and runs the path again. The cycle is the path length.</summary>
        Loop = 0,

        /// <summary>Reverses and walks the path backwards. The cycle is twice the path length.</summary>
        PingPong = 1
    }
}
