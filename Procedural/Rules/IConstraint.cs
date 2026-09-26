using System.Collections.Generic;

namespace AlpineLib.Procedural.Rules {
    /// <summary>One named rule a generated subject must satisfy.</summary>
    public interface IConstraint<in T> {
        /// <summary>Short name that prefixes every violation this rule reports.</summary>
        string Name { get; }

        /// <summary>Appends one message per violation found in <paramref name="subject"/>; appends nothing when satisfied.</summary>
        void Check(T subject, List<string> violations);
    }
}
