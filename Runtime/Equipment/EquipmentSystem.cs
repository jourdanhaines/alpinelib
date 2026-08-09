using System;
using AlpineLib.Actors;
using AlpineLib.Skills;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Holds the actor's currently equipped weapon and owns everything that weapon projects onto the
    /// actor: the locomotion override, the attached visual instance, the implicit stat modifiers, the
    /// granted skills, and the weapon damage feed the skill system reads.
    /// </summary>
    /// <remarks>
    /// Equipping is destructive-then-additive: <see cref="Equip"/> always unequips first, so the
    /// system can never accumulate two weapons' worth of modifiers or skills. Every side effect is
    /// keyed on this component as its source, which is what makes the reversal in
    /// <see cref="Unequip"/> exact rather than best-effort.
    ///
    /// The animator is expected to already run the actor's base controller when the first weapon is
    /// equipped: that controller is cached once and restored on unequip, and a weapon's
    /// <see cref="WeaponDefinition.locomotionOverride"/> must therefore be an
    /// <see cref="AnimatorOverrideController"/> built on top of that same base controller. An
    /// override built on a different base swaps the state machine wholesale and will desynchronise
    /// any system driving the animator by state tag.
    ///
    /// Weapon damage is published as <see cref="SkillSystem.WeaponDamageProvider"/> rather than baked
    /// into skills, so a skill asset stays weapon-agnostic and picks up whatever is held at cast
    /// time. A missing <see cref="SkillSystem"/> is tolerated: the actor simply gains no skills and
    /// no damage feed, which is the normal case for props and non-combat actors.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class EquipmentSystem : ActorSubsystem {
        /// <summary>
        /// Weapon currently held, or null when the actor is unarmed.
        /// </summary>
        public WeaponDefinition EquippedWeapon { get; private set; }

        /// <summary>
        /// Raised after a weapon is equipped or unequipped, carrying the new weapon or null.
        /// </summary>
        /// <remarks>
        /// Raised after every side effect has been applied or reversed, so handlers can read
        /// <see cref="EquippedWeapon"/> and the actor's stats and see the settled state.
        /// </remarks>
        public event Action<WeaponDefinition> OnWeaponChanged;

        private Actor _actor;
        private StatSheet _stats;
        private SkillSystem _skills;
        private GameObject _visualInstance;
        private RuntimeAnimatorController _originalController;
        private bool _hasCachedController;

        protected override void Start() {
            base.Start();

            _actor = GetComponent<Actor>();
            _stats = GetComponent<StatSheet>();
            _skills = GetComponent<SkillSystem>();
        }

        /// <summary>
        /// Equips a weapon, replacing whatever was held. Null is ignored — call
        /// <see cref="Unequip"/> to go unarmed.
        /// </summary>
        /// <remarks>
        /// Re-equipping the weapon already held is a full unequip/equip cycle: the visual is
        /// destroyed and respawned and the modifiers are reapplied, which is the cheap way to pick up
        /// edits to the definition at runtime.
        /// </remarks>
        public void Equip(WeaponDefinition weapon) {
            if (weapon == null) return;

            Unequip();

            EquippedWeapon = weapon;

            ApplyLocomotionOverride(weapon);
            SpawnVisual(weapon);
            ApplyImplicitModifiers(weapon);
            GrantWeaponSkills(weapon);

            OnWeaponChanged?.Invoke(weapon);
        }

        /// <summary>
        /// Removes the held weapon and every effect it applied. Does nothing when already unarmed.
        /// </summary>
        public void Unequip() {
            if (EquippedWeapon == null) return;

            RemoveWeaponSkills();
            _stats.RemoveModifiersFrom(this);
            DestroyVisual();
            RestoreLocomotionOverride();

            EquippedWeapon = null;
            OnWeaponChanged?.Invoke(null);
        }

        private void ApplyLocomotionOverride(WeaponDefinition weapon) {
            if (_actor.Animator == null) return;

            if (!_hasCachedController) {
                _originalController = _actor.Animator.runtimeAnimatorController;
                _hasCachedController = true;
            }

            if (weapon.locomotionOverride == null) return;
            _actor.Animator.runtimeAnimatorController = weapon.locomotionOverride;
        }

        private void RestoreLocomotionOverride() {
            if (!_hasCachedController) return;
            if (_actor.Animator == null) return;

            _actor.Animator.runtimeAnimatorController = _originalController;
        }

        private void SpawnVisual(WeaponDefinition weapon) {
            if (weapon.visualPrefab == null) return;

            var attachPoint = ResolveAttachPoint(weapon);
            if (attachPoint == null) return;

            _visualInstance = Instantiate(weapon.visualPrefab, attachPoint);
            _visualInstance.transform.localPosition = Vector3.zero;
            _visualInstance.transform.localRotation = Quaternion.identity;
            _visualInstance.transform.localScale = Vector3.one;
        }

        private void DestroyVisual() {
            if (_visualInstance == null) return;

            Destroy(_visualInstance);
            _visualInstance = null;
        }

        /// <summary>
        /// Resolves the transform a weapon visual attaches to, falling back to the animator root when
        /// the named bone is missing so a rig mismatch loses the attachment point rather than the
        /// weapon.
        /// </summary>
        private Transform ResolveAttachPoint(WeaponDefinition weapon) {
            if (_actor.Animator == null) {
                Debug.LogWarning($"{name} has no animator; weapon visual for '{weapon.displayName}' was not spawned.", this);
                return null;
            }

            var animatorRoot = _actor.Animator.transform;
            if (string.IsNullOrEmpty(weapon.attachBoneName)) return animatorRoot;

            var bone = FindDescendant(animatorRoot, weapon.attachBoneName);
            if (bone != null) return bone;

            Debug.LogWarning($"{name} has no bone named '{weapon.attachBoneName}'; attaching '{weapon.displayName}' to the animator root instead.", this);
            return animatorRoot;
        }

        /// <summary>
        /// Depth-first search for a descendant transform by exact name, including the root itself.
        /// </summary>
        /// <returns>The matching transform, or null when the hierarchy contains no such name.</returns>
        private static Transform FindDescendant(Transform root, string name) {
            if (root.name == name) return root;

            foreach (Transform child in root) {
                var found = FindDescendant(child, name);
                if (found != null) return found;
            }

            return null;
        }

        private void ApplyImplicitModifiers(WeaponDefinition weapon) {
            if (weapon.implicitModifiers == null) return;

            foreach (var modifier in weapon.implicitModifiers) {
                _stats.AddModifier(CreateSourcedModifier(modifier));
            }
        }

        /// <summary>
        /// Re-creates an authored modifier with this system as its source, preserving the tags that
        /// decide which damage queries it applies to.
        /// </summary>
        private StatModifier CreateSourcedModifier(StatModifier authored) {
            return new StatModifier(authored.Stat, authored.Operation, authored.Value, this, authored.Priority) {
                Tags = authored.Tags
            };
        }

        private void GrantWeaponSkills(WeaponDefinition weapon) {
            if (_skills == null) return;

            GrantGrantedSkills(weapon);
            _skills.WeaponDamageProvider = skill => skill.addWeaponDamage ? EquippedWeapon.baseDamage : 0f;
        }

        private void GrantGrantedSkills(WeaponDefinition weapon) {
            if (weapon.grantedSkills == null) return;

            foreach (var skill in weapon.grantedSkills) {
                _skills.GrantSkill(skill, this);
            }
        }

        private void RemoveWeaponSkills() {
            if (_skills == null) return;

            _skills.RemoveSkillsFrom(this);
            _skills.WeaponDamageProvider = null;
        }
    }
}
