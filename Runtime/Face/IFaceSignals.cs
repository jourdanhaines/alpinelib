using AlpineLib.Procedural.Expressions;

namespace AlpineLib.Face {
    /// <summary>
    /// Where a <see cref="FaceExpressionDriver"/> reads what its body is doing: a game adapter turns its own
    /// actor, gait and held-prop state into one <see cref="FaceSignalFrame"/> per step.
    /// </summary>
    /// <remarks>
    /// Sampled exactly once per driver step, so an adapter may report edges (a jump, a landing) as true for
    /// that one sample and clear them afterwards.
    /// </remarks>
    public interface IFaceSignals {
        /// <summary>The body's signals for a step of <paramref name="deltaTime"/> seconds.</summary>
        FaceSignalFrame Sample(float deltaTime);
    }
}
