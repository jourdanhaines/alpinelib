using System;
using System.Collections.Generic;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Body {
    /// <summary>
    /// A single wound sitting on a body part. Its identity is the <see cref="InjuryDefinition"/> it
    /// was created from, and everything it does — bleeding, stat debuffs, timed conditions — comes
    /// from that asset scaled by <see cref="Severity"/>.
    /// </summary>
    public class Injury {
        /// <summary>
        /// Runtime progress of one <see cref="InjuryCondition"/> that has set in on an injury.
        /// </summary>
        public class ConditionState {
            /// <summary>
            /// Authored condition this state is progressing.
            /// </summary>
            public InjuryCondition Condition { get; }

            /// <summary>
            /// How far the condition has run, from zero to one.
            /// </summary>
            public float Progress { get; private set; }

            /// <summary>
            /// True once the condition has run its course.
            /// </summary>
            public bool IsComplete => Progress >= 1f;

            internal ConditionState(InjuryCondition condition) {
                Condition = condition;
            }

            /// <summary>
            /// Advances progress and reports whether this tick was the one that completed it.
            /// </summary>
            internal bool Tick(float deltaTime, float severity) {
                if (IsComplete) return false;

                Progress = Mathf.Min(1f, Progress + deltaTime * Condition.progressRate * severity);
                return IsComplete;
            }
        }

        /// <summary>
        /// Asset this injury was created from. Two injuries are of the same kind when this matches.
        /// </summary>
        public InjuryDefinition Definition { get; }

        /// <summary>
        /// Part this injury sits on. Assigned by <see cref="BodySystem.ApplyInjury"/>.
        /// </summary>
        public BodyPartDefinition Location { get; set; }

        /// <summary>
        /// How bad this instance is. Scales bleeding and condition progress.
        /// </summary>
        public float Severity { get; }

        /// <summary>
        /// Health drained per second before the body part's multiplier. Zero once bandaged.
        /// </summary>
        public float BleedRate { get; private set; }

        /// <summary>
        /// True once <see cref="Bandage"/> has stopped the bleeding.
        /// </summary>
        public bool IsBandaged { get; private set; }

        /// <summary>
        /// Stat debuffs this injury applies while it is present, as authored on the definition.
        /// </summary>
        public StatModifier[] Modifiers { get; }

        private readonly List<ConditionState> _conditions = new();

        /// <summary>
        /// Conditions that have set in on this injury. Conditions that failed their onset roll are
        /// never listed.
        /// </summary>
        public IReadOnlyList<ConditionState> Conditions => _conditions;

        /// <summary>
        /// Raised once per condition, on the tick its progress reaches one.
        /// </summary>
        public event Action<ConditionState> OnConditionCompleted;

        public Injury(InjuryDefinition definition, float severity) {
            Definition = definition;
            Severity = severity;
            BleedRate = definition.baseBleedRate * severity;
            Modifiers = definition.statModifiers;

            RollConditions();
        }

        /// <summary>
        /// Stops the bleeding for good.
        /// </summary>
        public void Bandage() {
            IsBandaged = true;
            BleedRate = 0f;
        }

        /// <summary>
        /// Starts a condition on this injury regardless of its onset chance, for treatments and
        /// scripted events. Does nothing when the condition has already set in.
        /// </summary>
        public void ApplyCondition(InjuryCondition condition) {
            if (HasCondition(condition)) return;

            _conditions.Add(new ConditionState(condition));
        }

        /// <summary>
        /// True when the given condition has set in on this injury.
        /// </summary>
        public bool HasCondition(InjuryCondition condition) {
            foreach (var state in _conditions) {
                if (state.Condition == condition) return true;
            }

            return false;
        }

        /// <summary>
        /// Advances every condition that has set in. Driven by the owning <see cref="BodySystem"/>.
        /// </summary>
        public void Tick(float deltaTime) {
            foreach (var state in _conditions) {
                if (!state.Tick(deltaTime, Severity)) continue;

                OnConditionCompleted?.Invoke(state);
            }
        }

        private void RollConditions() {
            foreach (var condition in Definition.conditions) {
                if (condition.onsetChance <= 0f) continue;
                if (UnityEngine.Random.value > condition.onsetChance) continue;

                ApplyCondition(condition);
            }
        }
    }
}
