using System;
using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Combat;
using AlpineLib.Perception;
using AlpineLib.Projectiles;
using AlpineLib.Stats;
using AlpineLib.Vitals;
using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// The actor's skill bar: holds every skill it has been granted, bills their resource costs,
    /// tracks their cooldowns, and drives the active one off the animator — opening a melee hit box
    /// inside its damage window, releasing a projectile volley at its spawn time, and resolving
    /// whatever it connects with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Progress is read from the animator rather than from a timer, exactly as
    /// <see cref="CombatSystem"/> does, so a skill stays in sync with the clip that is actually
    /// playing. A full-body skill is tracked on <c>fullBodyLayerIndex</c> through states tagged
    /// "Attack"; an upper-body skill is tracked on <c>upperBodyLayerIndex</c> through states tagged
    /// "UpperSkill". A skill whose trigger never reaches such a state never completes and holds the
    /// bar until something calls <see cref="CancelActive"/>.
    /// </para>
    /// <para>
    /// The two body domains differ in what they take from the actor. Full-body skills suppress
    /// locomotion outright and skip root motion unless the skill opts into it, so the actor moves
    /// only if the clip says so. Upper-body skills leave locomotion alone, blend the upper-body
    /// layer in over <c>upperBodyBlendSpeed</c>, and merely slow the actor with a temporary
    /// <see cref="ModifierOperation.Multiply"/> modifier for as long as they run.
    /// </para>
    /// <para>
    /// Costs are all-or-nothing: every entry is checked for affordability before any of them is
    /// spent, so a skill never half-pays. An actor with no <see cref="ResourceSet"/>, or with no pool
    /// for a costed resource, casts for free rather than being unable to cast — which is what lets
    /// simple enemies share skill assets with the player.
    /// </para>
    /// <para>
    /// This component must live on the prefab. <see cref="RootMotionForwarder"/> caches its
    /// <see cref="IRootMotionSuppressor"/> array in <c>Start</c>, so a skill system added to a live
    /// actor is never consulted and its full-body skills will slide.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class SkillSystem : ActorSubsystem, IRootMotionSuppressor, IHitBoxOwner {
        private const string FullBodyStateTag = "Attack";
        private const string UpperBodyStateTag = "UpperSkill";
        private const float SpiralVolleyEndTime = 0.9f;

        [Header("Delivery")]
        [Tooltip("Hit box opened during a melee skill's damage window")]
        [SerializeField] private HitBox hitBox;

        [Tooltip("Socket projectiles are spawned from; falls back to one unit above the actor's feet")]
        [SerializeField] private Transform projectileOrigin;

        [Header("Animator Layers")]
        [Tooltip("Animator layer full body skills play on")]
        [SerializeField] private int fullBodyLayerIndex;

        [Tooltip("Animator layer upper body skills play on")]
        [SerializeField] private int upperBodyLayerIndex = 1;

        [Tooltip("Layer weight units per second when blending the upper body layer in and out")]
        [SerializeField] private float upperBodyBlendSpeed = 8f;

        [Header("Stats")]
        [Tooltip("Stat slowed while an upper body skill is casting")]
        [SerializeField] private StatDefinition moveSpeedStat;

        [Tooltip("Stat skill damage is evaluated against, filtered by the skill's tags")]
        [SerializeField] private StatDefinition damageStat;

        [Tooltip("Optional stat scaling resource costs; leave empty to bill costs unscaled")]
        [SerializeField] private StatDefinition costMultiplierStat;

        /// <summary>
        /// Every skill this actor currently has, in grant order. Includes skills that are on
        /// cooldown or unaffordable.
        /// </summary>
        public IReadOnlyList<SkillInstance> Skills => _skills;

        /// <summary>
        /// Number of hotbar slots that have been assigned, including any that were later cleared.
        /// </summary>
        public int SlotCount => _slots.Count;

        /// <summary>The skill currently running, or null when the bar is idle.</summary>
        public SkillInstance ActiveSkill => _active;

        /// <summary>True from the moment a skill starts until it finishes or is cancelled.</summary>
        public bool IsUsingSkill => _active != null;

        /// <summary>
        /// True while the active skill owns the actor's movement, so a controller should stop feeding
        /// it input. Only full-body skills block; upper-body skills merely slow the actor.
        /// </summary>
        public bool BlocksMovement => _active != null && _active.Definition.bodyDomain == SkillBodyDomain.FullBody;

        /// <inheritdoc />
        /// <remarks>
        /// Only full-body skills suppress, and only when they have not opted into root motion — a
        /// lunge is expected to carry the actor, a stationary swing is not.
        /// </remarks>
        public bool IsSuppressingRootMotion => BlocksMovement && !_active.Definition.useRootMotion;

        /// <summary>
        /// Degrees of turn the active skill still allows. Zero when no skill is running, so
        /// controllers can use it to decide whether they may keep tracking a moving target.
        /// </summary>
        public float RemainingAttackRotation => _active != null ? _active.Definition.maxRotation - _rotationUsed : 0f;

        /// <summary>
        /// Supplies the equipped weapon's damage for skills with
        /// <see cref="SkillDefinition.addWeaponDamage"/> set. Left unset — or returning zero — the
        /// skill contributes only its own base damage.
        /// </summary>
        /// <remarks>
        /// A delegate rather than a reference to an equipment component, so the skill module stays
        /// independent of how a game decides what is being held. Whatever owns equipment assigns it,
        /// normally on start.
        /// </remarks>
        public Func<SkillDefinition, float> WeaponDamageProvider;

        /// <summary>Raised when a skill begins, after its costs are paid and before any payload lands.</summary>
        public event Action<SkillDefinition> OnSkillStarted;

        /// <summary>Raised for each melee hit that lands, after the injury has been applied.</summary>
        public event Action<SkillDefinition, HurtBox> OnSkillHit;

        /// <summary>Raised when a skill ends, whether it landed, missed, or was cancelled.</summary>
        public event Action<SkillDefinition> OnSkillFinished;

        private readonly List<SkillInstance> _skills = new();
        private readonly List<SkillInstance> _slots = new();

        private Actor _actor;
        private ResourceSet _resources;
        private SkillInstance _active;

        private bool _enteredSkillState;
        private bool _hitBoxActive;
        private bool _damageWindowSpent;
        private bool _hasLanded;
        private int _projectilesFired;
        private float _rotationUsed;
        private Quaternion _lastRotation;
        private float _upperBodyWeight;

        protected override void Start() {
            base.Start();

            _actor = GetComponent<Actor>();
            _resources = GetComponent<ResourceSet>();

            if (hitBox != null)
                hitBox.Init(this);
        }

        protected override void OnOwnerDeath() {
            CancelActive();
            base.OnOwnerDeath();
        }

        /// <summary>
        /// Adds a skill to this actor, attributed to whatever granted it. Granting the same
        /// definition twice from the same source is ignored; granting it from two different sources
        /// produces two independent instances with their own cooldowns.
        /// </summary>
        /// <param name="skill">Definition to grant. Null is ignored.</param>
        /// <param name="source">Object responsible for the grant, used later by <see cref="RemoveSkillsFrom"/>.</param>
        public void GrantSkill(SkillDefinition skill, object source) {
            if (skill == null) return;
            if (FindInstance(skill, source) != null) return;

            _skills.Add(new SkillInstance(skill, source));
        }

        /// <summary>
        /// Revokes every skill granted by one source — un-equipping a weapon, refunding a passive.
        /// Slots pointing at a revoked skill are cleared, and a revoked skill that is mid-use is
        /// cancelled.
        /// </summary>
        /// <param name="source">The granting object to revoke. Compared by reference.</param>
        public void RemoveSkillsFrom(object source) {
            if (_active != null && _active.Source == source)
                CancelActive();

            _skills.RemoveAll(instance => instance.Source == source);
            ClearSlotsOfRevokedSkills();
        }

        /// <summary>
        /// Points a hotbar slot at a skill, growing the bar as needed. A skill the actor does not
        /// already have is granted to it first, with this system as the source, so assigning a slot
        /// is enough to make the skill usable.
        /// </summary>
        /// <param name="slot">Zero-based slot index. Negative indices are ignored.</param>
        /// <param name="skill">Skill to place in the slot, or null to clear it.</param>
        public void AssignSlot(int slot, SkillDefinition skill) {
            if (slot < 0) return;

            while (_slots.Count <= slot) {
                _slots.Add(null);
            }

            _slots[slot] = skill == null ? null : ResolveOrGrant(skill);
        }

        /// <summary>
        /// The instance in a hotbar slot, or null when the slot is empty or out of range.
        /// </summary>
        public SkillInstance GetSlot(int slot) {
            if (slot < 0 || slot >= _slots.Count) return null;

            return _slots[slot];
        }

        /// <summary>
        /// Uses whatever is in a hotbar slot.
        /// </summary>
        /// <returns>True when the skill started; false for an empty slot or any failed guard.</returns>
        public bool TryUseSlot(int slot) {
            var instance = GetSlot(slot);
            if (instance == null) return false;

            return TryUseInstance(instance);
        }

        /// <summary>
        /// Uses a skill by definition, granting it to this actor first if it does not already have
        /// it, so that its cooldown is tracked like any other.
        /// </summary>
        /// <returns>
        /// True when the skill started. False when the actor is dead, another skill is already
        /// running, this one is still on cooldown, or its costs cannot be paid.
        /// </returns>
        public bool TryUse(SkillDefinition skill) {
            if (skill == null) return false;

            return TryUseInstance(ResolveOrGrant(skill));
        }

        /// <summary>
        /// Interrupts the running skill, if any. The cooldown still applies, spent resources are not
        /// refunded, and the skill's animator trigger is reset so a queued re-entry cannot fire it
        /// again.
        /// </summary>
        public void CancelActive() {
            if (_active == null) return;

            ResetAnimatorTrigger(_active.Definition);
            FinishSkill();
        }

        /// <summary>
        /// Called by <see cref="HitBox"/> for every hurt box a melee skill's live damage window
        /// overlaps. Applies the skill's damage packet and closes the window behind it.
        /// </summary>
        /// <remarks>
        /// Hits on the attacker's own body are filtered out here, before the packet is built, so a
        /// hit box that clips its owner costs nothing. The window is closed on the first landed hit,
        /// matching <see cref="CombatSystem.OnHitBoxContact"/>: one skill use is one hit.
        /// </remarks>
        public void OnHitBoxContact(HurtBox hurtBox) {
            if (_active == null) return;
            if (hurtBox == null) return;
            if (hurtBox.Owner.transform.root == transform.root) return;

            var skill = _active.Definition;
            DamageResolver.Apply(BuildDamagePacket(skill), hurtBox);
            OnSkillHit?.Invoke(skill, hurtBox);

            _hasLanded = true;
            CloseDamageWindow();
        }

        private void Update() {
            TickCooldowns();
            TickUpperBodyWeight();

            if (_active == null) return;

            TrackRotation();
            TickActiveSkill();
        }

        private void TickCooldowns() {
            foreach (var instance in _skills) {
                if (instance.CooldownRemaining <= 0f) continue;

                instance.CooldownRemaining = Mathf.Max(0f, instance.CooldownRemaining - Time.deltaTime);
            }
        }

        private void TickUpperBodyWeight() {
            var animator = _actor.Animator;
            if (animator == null) return;
            if (upperBodyLayerIndex < 0 || upperBodyLayerIndex >= animator.layerCount) return;

            float targetWeight = IsUpperBodySkillActive() ? 1f : 0f;
            if (Mathf.Approximately(_upperBodyWeight, targetWeight)) return;

            _upperBodyWeight = Mathf.MoveTowards(_upperBodyWeight, targetWeight, upperBodyBlendSpeed * Time.deltaTime);
            animator.SetLayerWeight(upperBodyLayerIndex, _upperBodyWeight);
        }

        private void TrackRotation() {
            _rotationUsed += Quaternion.Angle(_lastRotation, transform.rotation);
            _lastRotation = transform.rotation;
        }

        private void TickActiveSkill() {
            if (_actor.Animator == null) return;

            if (_active.Definition.bodyDomain == SkillBodyDomain.FullBody) {
                TickAnimatorDrivenSkill(fullBodyLayerIndex, FullBodyStateTag);
                return;
            }

            TickAnimatorDrivenSkill(upperBodyLayerIndex, UpperBodyStateTag);
        }

        /// <remarks>
        /// The entered/left latch is the same one <see cref="CombatSystem"/> uses: the skill is not
        /// considered finished until the animator has actually reached the tagged state and then
        /// left it, which tolerates the transition frames between the trigger firing and the clip
        /// starting.
        /// </remarks>
        private void TickAnimatorDrivenSkill(int layerIndex, string stateTag) {
            var stateInfo = _actor.Animator.GetCurrentAnimatorStateInfo(layerIndex);
            bool inSkillState = stateInfo.IsTag(stateTag);

            if (inSkillState)
                _enteredSkillState = true;

            if (_enteredSkillState && !inSkillState) {
                FinishSkill();
                return;
            }

            if (!inSkillState) return;

            TickSkillPayload(stateInfo.normalizedTime % 1f);

            if (stateInfo.normalizedTime >= 1f)
                FinishSkill();
        }

        private void TickSkillPayload(float normalizedTime) {
            if (_active.Definition is MeleeSkillDefinition meleeSkill) {
                TickMeleeWindow(meleeSkill, normalizedTime);
                return;
            }

            if (_active.Definition is ProjectileSkillDefinition projectileSkill)
                TickProjectileVolley(projectileSkill, normalizedTime);
        }

        private void TickMeleeWindow(MeleeSkillDefinition skill, float normalizedTime) {
            if (hitBox == null) return;

            if (_hitBoxActive && normalizedTime >= skill.damageWindowEnd) {
                CloseDamageWindow();
                return;
            }

            if (_hitBoxActive || _damageWindowSpent || _hasLanded) return;
            if (normalizedTime < skill.damageWindowStart) return;
            if (normalizedTime >= skill.damageWindowEnd) return;

            _hitBoxActive = true;
            hitBox.Activate();
        }

        private void TickProjectileVolley(ProjectileSkillDefinition skill, float normalizedTime) {
            if (skill.projectilePrefab == null) return;

            int shotCount = Mathf.Max(1, skill.projectileCount);

            while (_projectilesFired < shotCount && normalizedTime >= GetLaunchTime(skill, _projectilesFired, shotCount)) {
                LaunchProjectile(skill, _projectilesFired, shotCount);
                _projectilesFired++;
            }
        }

        /// <remarks>
        /// Fan and single volleys release everything on the same frame. A spiral spreads its releases
        /// evenly from the spawn time to <see cref="SpiralVolleyEndTime"/>, leaving the tail of the
        /// clip free so the last shot is not cut off by the state ending.
        /// </remarks>
        private static float GetLaunchTime(ProjectileSkillDefinition skill, int index, int count) {
            if (skill.pattern != ProjectilePattern.Spiral) return skill.spawnTimeNormalized;

            float volleyWindow = Mathf.Max(0f, SpiralVolleyEndTime - skill.spawnTimeNormalized);
            return skill.spawnTimeNormalized + index * (volleyWindow / count);
        }

        private void LaunchProjectile(ProjectileSkillDefinition skill, int index, int count) {
            Vector3 origin = projectileOrigin != null ? projectileOrigin.position : transform.position + Vector3.up;
            Vector3 direction = ProjectilePatterns.GetDirection(skill.pattern, index, count, transform.forward, Vector3.up, skill.spreadAngle);

            var projectile = Instantiate(skill.projectilePrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
            projectile.Launch(origin, direction, skill.projectileSpeed, skill.projectileLifetime, BuildDamagePacket(skill), gameObject);
        }

        /// <remarks>
        /// Damage is resolved at the moment the hit or the launch happens, not when the skill starts,
        /// so a buff that lands mid-animation is counted. The skill's tags are the query context, so
        /// only modifiers whose own tags are a subset of them contribute.
        /// </remarks>
        private DamagePacket BuildDamagePacket(SkillDefinition skill) {
            float weaponDamage = skill.addWeaponDamage ? WeaponDamageProvider?.Invoke(skill) ?? 0f : 0f;
            float baseDamage = skill.baseDamage + weaponDamage;
            float amount = damageStat != null ? _actor.Stats.Evaluate(damageStat, skill.tags, baseDamage) : baseDamage;

            return new DamagePacket(amount, skill.tags, skill.injury, skill.injurySeverity, gameObject);
        }

        private bool TryUseInstance(SkillInstance instance) {
            if (Owner == null || !Owner.IsAlive) return false;
            if (IsUsingSkill) return false;
            if (!instance.IsReady) return false;
            if (!TrySpendCosts(instance.Definition)) return false;

            StartSkill(instance);
            return true;
        }

        /// <remarks>
        /// Two passes on purpose: a skill costing mana and stamina must not drain the mana of an
        /// actor who cannot also pay the stamina.
        /// </remarks>
        private bool TrySpendCosts(SkillDefinition skill) {
            if (skill.costs == null || skill.costs.Length == 0) return true;
            if (_resources == null) return true;

            float costMultiplier = costMultiplierStat != null ? _actor.Stats.Get(costMultiplierStat, skill.tags) : 1f;

            foreach (var cost in skill.costs) {
                if (!CanAfford(cost, costMultiplier)) return false;
            }

            foreach (var cost in skill.costs) {
                _resources.Get(cost.resource)?.Spend(cost.amount * costMultiplier);
            }

            return true;
        }

        private bool CanAfford(ResourceCost cost, float costMultiplier) {
            var pool = _resources.Get(cost.resource);
            if (pool == null) return true;

            return pool.CurrentValue >= cost.amount * costMultiplier;
        }

        private void StartSkill(SkillInstance instance) {
            var skill = instance.Definition;

            _active = instance;
            _enteredSkillState = false;
            _hitBoxActive = false;
            _damageWindowSpent = false;
            _hasLanded = false;
            _projectilesFired = 0;
            _rotationUsed = 0f;
            _lastRotation = transform.rotation;

            if (skill.bodyDomain == SkillBodyDomain.FullBody)
                _actor.SuppressLocomotion();
            else
                ApplyCastSlow(skill);

            if (skill.noiseRadius > 0f)
                NoiseEmitter.Emit(gameObject, transform.position, skill.noiseRadius);

            if (_actor.Animator != null && !string.IsNullOrEmpty(skill.animationTrigger))
                _actor.Animator.SetTrigger(skill.animationTrigger);

            OnSkillStarted?.Invoke(skill);
        }

        private void FinishSkill() {
            CloseDamageWindow();

            var skill = _active.Definition;

            if (skill.bodyDomain == SkillBodyDomain.FullBody)
                _actor.ResumeLocomotion();
            else
                _actor.Stats.RemoveModifiersFrom(this);

            _active.CooldownRemaining = skill.cooldown;
            _active = null;

            OnSkillFinished?.Invoke(skill);
        }

        private void ApplyCastSlow(SkillDefinition skill) {
            if (moveSpeedStat == null) return;

            _actor.Stats.AddModifier(new StatModifier(moveSpeedStat, ModifierOperation.Multiply, skill.castMoveSpeedMultiplier, this));
        }

        private void CloseDamageWindow() {
            if (!_hitBoxActive) return;

            _hitBoxActive = false;
            _damageWindowSpent = true;
            hitBox.Deactivate();
        }

        private void ResetAnimatorTrigger(SkillDefinition skill) {
            if (_actor.Animator == null) return;
            if (string.IsNullOrEmpty(skill.animationTrigger)) return;

            _actor.Animator.ResetTrigger(skill.animationTrigger);
        }

        private bool IsUpperBodySkillActive() {
            return _active != null && _active.Definition.bodyDomain == SkillBodyDomain.UpperBody;
        }

        private SkillInstance ResolveOrGrant(SkillDefinition skill) {
            var existing = FindInstance(skill);
            if (existing != null) return existing;

            var granted = new SkillInstance(skill, this);
            _skills.Add(granted);
            return granted;
        }

        private SkillInstance FindInstance(SkillDefinition skill) {
            foreach (var instance in _skills) {
                if (instance.Definition == skill) return instance;
            }

            return null;
        }

        private SkillInstance FindInstance(SkillDefinition skill, object source) {
            foreach (var instance in _skills) {
                if (instance.Definition == skill && instance.Source == source) return instance;
            }

            return null;
        }

        private void ClearSlotsOfRevokedSkills() {
            for (int slot = 0; slot < _slots.Count; slot++) {
                if (_slots[slot] == null) continue;
                if (_skills.Contains(_slots[slot])) continue;

                _slots[slot] = null;
            }
        }
    }
}
