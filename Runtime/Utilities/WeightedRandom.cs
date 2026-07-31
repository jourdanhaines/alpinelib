using System;
using System.Collections.Generic;

namespace AlpineLib.Utilities {
    /// <summary>
    /// Random selection helpers that honour a per-item weight.
    /// </summary>
    public static class WeightedRandom {
        /// <summary>
        /// Picks one item at random, each item's chance being proportional to its weight.
        /// Weights are expected to be zero or greater; negative weights make the distribution meaningless.
        /// </summary>
        /// <typeparam name="T">Type of the candidate items.</typeparam>
        /// <param name="items">Candidates to pick from. Arrays and lists both satisfy this parameter.</param>
        /// <param name="weightSelector">Returns the relative weight of a candidate.</param>
        /// <returns>The selected item, or the type default when there are no candidates.</returns>
        public static T Pick<T>(IReadOnlyList<T> items, Func<T, float> weightSelector) {
            if (items.Count == 0) return default;

            float totalWeight = 0f;
            for (int index = 0; index < items.Count; index++) {
                totalWeight += weightSelector(items[index]);
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulativeWeight = 0f;

            for (int index = 0; index < items.Count; index++) {
                cumulativeWeight += weightSelector(items[index]);
                if (roll <= cumulativeWeight) return items[index];
            }

            // Floating point drift can leave the roll just past the final cumulative weight.
            return items[items.Count - 1];
        }
    }
}
