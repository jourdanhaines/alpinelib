using System.Collections.Generic;
using AlpineLib.Procedural.Rules;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class ConstraintSetTests {
        [Fact]
        public void EmptySetIsSatisfied() {
            ConstraintReport report = new ConstraintSet<int>().Evaluate(5);

            Assert.True(report.IsSatisfied);
            Assert.Equal("satisfied", report.ToString());
        }

        [Fact]
        public void ViolationsArePrefixedInRuleOrder() {
            var rules = new ConstraintSet<int>()
                .Add("positive", (value, violations) => AddIf(value <= 0, violations, $"{value} is not positive"))
                .Add(new EvenConstraint())
                .Add("small", (value, violations) => AddIf(value > 10, violations, "too big"));

            ConstraintReport report = rules.Evaluate(-3);

            Assert.False(report.IsSatisfied);
            Assert.Equal(new[] { "positive: -3 is not positive", "even: -3 is odd" }, report.Violations);
            Assert.Equal("2 violations:\n  - positive: -3 is not positive\n  - even: -3 is odd", report.ToString());
        }

        [Fact]
        public void OneRuleMayReportSeveralViolations() {
            var rules = new ConstraintSet<string>()
                .Add("chars", (text, violations) => AddEachDigit(text, violations));

            ConstraintReport report = rules.Evaluate("a1b2");

            Assert.Equal(new[] { "chars: digit 1", "chars: digit 2" }, report.Violations);
        }

        [Fact]
        public void SatisfiedSubjectPasses() {
            var rules = new ConstraintSet<int>().Add(new EvenConstraint());

            Assert.True(rules.Evaluate(4).IsSatisfied);
        }

        private static void AddIf(bool condition, List<string> violations, string message) {
            if (condition) {
                violations.Add(message);
            }
        }

        private static void AddEachDigit(string text, List<string> violations) {
            foreach (char character in text) {
                AddIf(char.IsDigit(character), violations, $"digit {character}");
            }
        }
    }
}
