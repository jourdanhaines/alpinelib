using System;
using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Body {
    /// <summary>
    /// Per-actor anatomical damage model: builds a set of <see cref="BodyPart"/>s from a body plan,
    /// holds the injuries applied to each, applies their stat debuffs, and ticks bleeding and injury
    /// conditions while the owner is alive.
    /// </summary>
    /// <remarks>
    /// The system never touches health itself. Bleeding and hit damage are reported through
    /// <see cref="OnDamageTick"/>, and the game decides what that damage means — a health need, a
    /// hit point pool, nothing at all.
    /// </remarks>
    public class BodySystem : ActorSubsystem {
        [Tooltip("Anatomy this body is built from")]
        [SerializeField] private BodyPlanDefinition bodyPlan;

        private readonly Dictionary<BodyPartDefinition, BodyPart> _parts = new();
        private StatSheet _stats;

        /// <summary>
        /// Raised after an injury has been added to a part and its modifiers applied.
        /// </summary>
        public event Action<Injury> OnInjuryApplied;

        /// <summary>
        /// Raised after an injury has been removed from a part and its modifiers withdrawn.
        /// </summary>
        public event Action<Injury> OnInjuryRemoved;

        /// <summary>
        /// Damage this body has taken, in health units. Raised once per frame while anything is
        /// bleeding, and once per hit that carries damage.
        /// </summary>
        public event Action<float> OnDamageTick;

        /// <summary>
        /// Parts of this body, keyed by the definitions of the body plan it was built from.
        /// </summary>
        public IReadOnlyDictionary<BodyPartDefinition, BodyPart> Parts => _parts;

        protected override void Start() {
            base.Start();

            _stats = GetComponent<StatSheet>();
            InitializeParts();
        }

        private void Update() {
            float totalBleed = 0f;

            foreach (var part in _parts.Values) {
                foreach (var injury in part.Injuries) {
                    totalBleed += injury.BleedRate * part.SeverityMultiplier;
                    injury.Tick(Time.deltaTime);
                }
            }

            if (totalBleed > 0f) {
                OnDamageTick?.Invoke(totalBleed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Adds an injury to a part, applies its stat debuffs and reports the hit damage scaled by
        /// the part's severity multiplier. Injuries on parts outside this body's plan are dropped.
        /// </summary>
        public void ApplyInjury(BodyPartDefinition location, Injury injury, float damage = 0f) {
            if (!_parts.TryGetValue(location, out var part)) return;

            injury.Location = location;
            part.AddInjury(injury);

            ApplyModifiers(injury);

            if (damage > 0f)
                OnDamageTick?.Invoke(damage * part.SeverityMultiplier);

            OnInjuryApplied?.Invoke(injury);
        }

        /// <summary>
        /// Removes an injury from a part and withdraws every stat modifier it applied.
        /// </summary>
        public void RemoveInjury(BodyPartDefinition location, Injury injury) {
            if (!_parts.TryGetValue(location, out var part)) return;

            part.RemoveInjury(injury);
            _stats.RemoveModifiersFrom(injury);

            OnInjuryRemoved?.Invoke(injury);
        }

        /// <summary>
        /// The runtime part for a definition, or null when this body has no such part.
        /// </summary>
        public BodyPart GetPart(BodyPartDefinition definition) {
            return _parts.TryGetValue(definition, out var part) ? part : null;
        }

        /// <summary>
        /// Injuries on one part, empty when this body has no such part.
        /// </summary>
        public IReadOnlyList<Injury> GetInjuries(BodyPartDefinition definition) {
            if (!_parts.TryGetValue(definition, out var part)) return Array.Empty<Injury>();

            return part.Injuries;
        }

        /// <summary>
        /// Every injury on this body, across all parts.
        /// </summary>
        public List<Injury> GetAllInjuries() {
            var all = new List<Injury>();

            foreach (var part in _parts.Values) {
                all.AddRange(part.Injuries);
            }

            return all;
        }

        private void ApplyModifiers(Injury injury) {
            if (injury.Modifiers == null) return;

            foreach (var modifier in injury.Modifiers) {
                var sourced = new StatModifier(modifier.Stat, modifier.Operation, modifier.Value, injury, modifier.Tags, modifier.Priority);
                _stats.AddModifier(sourced);
            }
        }

        private void InitializeParts() {
            foreach (var definition in bodyPlan.parts) {
                _parts[definition] = new BodyPart(definition);
            }
        }
    }
}
