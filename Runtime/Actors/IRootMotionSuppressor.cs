namespace AlpineLib.Actors {
    /// <summary>
    /// Implemented by components that temporarily own an actor's motion, such as an attack or a
    /// stagger reaction, so that <see cref="RootMotionForwarder"/> stops feeding animator root
    /// motion into the character controller while they are active.
    /// </summary>
    public interface IRootMotionSuppressor {
        /// <summary>
        /// True while this component is driving the actor and root motion must not be applied.
        /// </summary>
        bool IsSuppressingRootMotion { get; }
    }
}
