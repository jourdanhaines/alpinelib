using System.Collections.Generic;
using AlpineLib.Procedural.Rules;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class EvenConstraint : IConstraint<int> {
        public string Name => "even";

        public void Check(int subject, List<string> violations) {
            if (subject % 2 == 0) {
                return;
            }
            violations.Add($"{subject} is odd");
        }
    }
}
