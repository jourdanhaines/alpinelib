using System;
using System.Globalization;

namespace AlpineLib.Procedural.Rules {
    /// <summary>
    /// Deterministic retry: build a candidate from a derived stream, check it, and try the next stream
    /// until one satisfies the rules or the attempts run out.
    /// </summary>
    public static class LayoutAttempts {
        /// <summary>
        /// Builds up to <paramref name="maxAttempts"/> candidates. Attempt n (from 0) draws from
        /// <c>rng.Derive("attempt/n")</c>, so the outcome depends only on the seed, never on prior draws.
        /// On failure <paramref name="result"/> is the last candidate and <paramref name="lastReport"/> its report.
        /// </summary>
        public static bool TryBuild<T>(
            SplitMix64 rng,
            int maxAttempts,
            Func<SplitMix64, T> build,
            ConstraintSet<T> rules,
            out T result,
            out ConstraintReport lastReport) {
            if (rng == null) {
                throw new ArgumentNullException(nameof(rng));
            }
            if (build == null) {
                throw new ArgumentNullException(nameof(build));
            }
            if (rules == null) {
                throw new ArgumentNullException(nameof(rules));
            }
            if (maxAttempts < 1) {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Must be at least 1.");
            }
            result = default;
            lastReport = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++) {
                result = build(rng.Derive(AttemptStream(attempt)));
                lastReport = rules.Evaluate(result);
                if (lastReport.IsSatisfied) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The stream name attempt <paramref name="attempt"/> derives from.</summary>
        public static string AttemptStream(int attempt) {
            return "attempt/" + attempt.ToString(CultureInfo.InvariantCulture);
        }
    }
}
