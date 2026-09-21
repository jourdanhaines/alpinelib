namespace AlpineLib.Actors {
    /// <summary>
    /// Script execution order pins for the actor's own simulation.
    /// </summary>
    /// <remarks>
    /// The frame contract: anything that moves a carrier (a train, a lift) writes its transform before
    /// <see cref="Motor"/>; brains run at the default order and hand the actor this frame's intent;
    /// the motor then syncs physics transforms, steps, reparents and writes the render pose — all
    /// before the camera rigs' <c>LateUpdate</c> and before the networking pawn drivers at 50 sample it.
    /// In <c>LateUpdate</c>, once the animator has posed the skeleton and the actor has written its
    /// parameters, <see cref="PoseModifiers"/> lays procedural motion over the result.
    /// </remarks>
    public static class ActorExecutionOrder {
        /// <summary>The actor's fixed-step motor: after every default-order brain, before carriers publish.</summary>
        public const int Motor = 10;

        /// <summary>Procedural bone writes: after the actor's own <c>LateUpdate</c>, over the animated pose.</summary>
        public const int PoseModifiers = 20;
    }
}
