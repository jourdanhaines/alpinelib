using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>
    /// Scores candidate anchor cells for a <see cref="LandmarkField"/> tier with an attract chance: the lowest score
    /// inside the block's anchor window wins (row-major first on a tie); <see cref="int.MaxValue"/> means "not a
    /// candidate" and a window with none keeps the drawn anchor.
    /// </summary>
    /// <remarks>
    /// Must be a pure, deterministic function of its arguments (it runs on every runtime and thread, in any order).
    /// It must not ask the field for sites, or the field would recurse.
    /// </remarks>
    public interface ILandmarkAnchorBias {
        /// <summary>
        /// The score of <paramref name="candidate"/> as the anchor of the site in <paramref name="block"/> of tier
        /// <paramref name="tier"/>; <paramref name="preference"/> is a uniform [0, 1) draw of that site the scorer may
        /// use to vary what it prefers.
        /// </summary>
        int Score(int tier, CellCoord block, CellCoord candidate, double preference);
    }
}
