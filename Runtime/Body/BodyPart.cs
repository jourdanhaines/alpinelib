using System.Collections.Generic;

namespace AlpineLib.Body {
    /// <summary>
    /// One location on a body at runtime: the part it was built from and the injuries currently on it.
    /// </summary>
    public class BodyPart {
        /// <summary>
        /// Asset this part was built from.
        /// </summary>
        public BodyPartDefinition Definition { get; }

        /// <summary>
        /// How hard this part is hit relative to an average one.
        /// </summary>
        public float SeverityMultiplier => Definition.severityMultiplier;

        private readonly List<Injury> _injuries = new();

        /// <summary>
        /// Injuries currently on this part, in the order they were applied.
        /// </summary>
        public IReadOnlyList<Injury> Injuries => _injuries;

        public BodyPart(BodyPartDefinition definition) {
            Definition = definition;
        }

        public void AddInjury(Injury injury) {
            _injuries.Add(injury);
        }

        public void RemoveInjury(Injury injury) {
            _injuries.Remove(injury);
        }
    }
}
