using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Rules {
    /// <summary>An ordered list of rules evaluated together against one subject.</summary>
    public sealed class ConstraintSet<T> {
        private readonly List<IConstraint<T>> _constraints = new List<IConstraint<T>>();

        /// <summary>The rules in evaluation order.</summary>
        public IReadOnlyList<IConstraint<T>> Constraints => _constraints;

        /// <summary>Appends a rule; returns this set for chaining.</summary>
        public ConstraintSet<T> Add(IConstraint<T> constraint) {
            if (constraint == null) {
                throw new ArgumentNullException(nameof(constraint));
            }
            _constraints.Add(constraint);
            return this;
        }

        /// <summary>Appends a delegate rule; returns this set for chaining.</summary>
        public ConstraintSet<T> Add(string name, Action<T, List<string>> check) {
            return Add(new DelegateConstraint<T>(name, check));
        }

        /// <summary>Runs every rule against <paramref name="subject"/> and collects the violations.</summary>
        public ConstraintReport Evaluate(T subject) {
            var violations = new List<string>();
            var scratch = new List<string>();
            foreach (IConstraint<T> constraint in _constraints) {
                scratch.Clear();
                constraint.Check(subject, scratch);
                violations.AddRange(PrefixAll(constraint.Name, scratch));
            }
            return new ConstraintReport(violations);
        }

        private static IEnumerable<string> PrefixAll(string name, List<string> messages) {
            foreach (string message in messages) {
                yield return $"{name}: {message}";
            }
        }
    }
}
