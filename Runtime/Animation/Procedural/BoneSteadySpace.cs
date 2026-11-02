namespace AlpineLib.Animation.Procedural {
    /// <summary>What a <see cref="BoneSteadyPoseModifier"/>'s reference rotation is relative to.</summary>
    public enum BoneSteadySpace {
        /// <summary>The bone's parent: its local rotation is held; the parent's own sway still carries it.</summary>
        Parent,

        /// <summary>The actor root: the bone keeps its facing in the actor's frame whatever the bones below it do.</summary>
        Actor,
    }
}
