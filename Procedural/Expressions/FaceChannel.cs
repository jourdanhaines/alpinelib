namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// One pose a face can blend toward, 0 at rest and 1 at full strength. The expression channels
    /// (<see cref="Wide"/>, <see cref="Squint"/>, <see cref="Squash"/>, <see cref="Worried"/>) share one budget;
    /// <see cref="Blink"/> and the looks sit on top.
    /// </summary>
    public enum FaceChannel {
        Blink,
        Wide,
        Squint,
        Squash,
        Worried,
        LookLeft,
        LookRight,
        LookUp,
        LookDown,
    }
}
