using System;
using System.Collections.Generic;
using System.Text;

namespace AlpineLib.Procedural.Rules {
    /// <summary>The outcome of evaluating a <see cref="ConstraintSet{T}"/>: every violation, prefixed by its rule's name.</summary>
    public sealed class ConstraintReport {
        private readonly List<string> _violations;

        /// <summary>Builds a report from already-prefixed violation messages.</summary>
        public ConstraintReport(IEnumerable<string> violations) {
            if (violations == null) {
                throw new ArgumentNullException(nameof(violations));
            }
            _violations = new List<string>(violations);
        }

        /// <summary>True when no rule reported anything.</summary>
        public bool IsSatisfied => _violations.Count == 0;

        /// <summary>Messages of the form "&lt;constraint&gt;: &lt;message&gt;", in rule order.</summary>
        public IReadOnlyList<string> Violations => _violations;

        /// <summary>"satisfied", or the violation count followed by one violation per line.</summary>
        public override string ToString() {
            if (IsSatisfied) {
                return "satisfied";
            }
            var builder = new StringBuilder();
            builder.Append(_violations.Count).Append(_violations.Count == 1 ? " violation:" : " violations:");
            foreach (string violation in _violations) {
                builder.Append('\n').Append("  - ").Append(violation);
            }
            return builder.ToString();
        }
    }
}
