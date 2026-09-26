using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Rules {
    /// <summary>An <see cref="IConstraint{T}"/> backed by a delegate, for rules too small to deserve a class.</summary>
    public sealed class DelegateConstraint<T> : IConstraint<T> {
        private readonly Action<T, List<string>> _check;

        /// <summary>Wraps <paramref name="check"/> under <paramref name="name"/>.</summary>
        public DelegateConstraint(string name, Action<T, List<string>> check) {
            if (string.IsNullOrEmpty(name)) {
                throw new ArgumentException("A constraint needs a name.", nameof(name));
            }
            Name = name;
            _check = check ?? throw new ArgumentNullException(nameof(check));
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public void Check(T subject, List<string> violations) {
            _check(subject, violations);
        }
    }
}
