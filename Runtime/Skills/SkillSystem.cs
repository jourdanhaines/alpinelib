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
    /// A <see cref="MeleeSkillDefinition"/> runs as a chain of
    /// <see cref="MeleeComboStage"/> swings on whichever layer its body domain plays on — an
    /// upper-body chain keeps the base layer animating locomotion, which is how a weave attacks
    /// while the legs keep walking. Each stage names an animator trigger, and every stage must land
    /// in its own state carrying the domain's tag: the chain is followed by watching that layer's
    /// current state change from one tagged state to the next, so two stages sharing a state read
    /// as one swing. Stage movement is code-driven by the possessing controller, so staged skills
    /// suppress root motion per stage on any domain. Re-using the running skill while a stage is past its
    /// <see cref="MeleeComboStage.comboWindowStart"/> buffers an advance, which fires at
    /// <see cref="MeleeComboStage.comboAdvanceTime"/> — that must sit before the state's exit
    /// transition, which by convention leaves attack states at normalized time 0.9, or the animator
    /// falls back to locomotion first and the buffered press is dropped. The whole chain is billed
    /// and cooled down once, as a single use of the skill. A melee skill with no authored stages
    /// synthesizes one locked stage from its own trigger and window fields and therefore behaves
    /// exactly as it did before combos existed.
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
        private const float StageEntryTimeout = 1.5f;

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
        /// The combo stage currently playing, or null whenever no staged melee skill is running —
        /// including while an upper-body skill, a projectile skill or nothing at all is active.
        /// </summary>
        /// <remarks>
        /// This is the stage a controller reads for per-swing movement policy: turn rate cap,
        /// carried momentum and its drag. For a melee skill authored without stages it is the
        /// synthesized single stage, never null, so a controller does not need a separate legacy
        /// path.
        /// </remarks>
        public MeleeComboStage ActiveStage => IsStageIndexValid() ? _runtimeStages[_stageIndex] : null;

        /// <summary>
        /// Zero-based index of <see cref="ActiveStage"/> within its skill's chain, or -1 when no
        /// staged melee skill is running.
        /// </summary>
        public int ActiveStageIndex => _stageIndex;

        /// <summary>
        /// How much of the actor's movement the active skill claims, or null when the controller
        /// owns movement outright.
        /// </summary>
        /// <remarks>
        /// A staged melee skill reports its current stage's own
        /// <see cref="MeleeComboStage.locomotion"/> regardless of body domain — an upper-body combo
        /// still steers or carries momentum through its stages even though its legs keep animating.
        /// A non-staged full-body skill reports <see cref="StageLocomotion.Locked"/>, the behaviour
        /// full-body skills have always had. Non-staged upper-body skills report null rather than a
        /// mode, because they never claimed movement in the first place — they only slow it.
        /// </remarks>
        public StageLocomotion? ActiveStageLocomotion {
            get {
                var stage = ActiveStage;
                if (stage != null) return stage.locomotion;

                return IsFullBodySkillActive() ? StageLocomotion.Locked : (StageLocomotion?)null;
            }
        }

        /// <summary>
        /// True while the active skill owns the actor's movement, so a controller should stop feeding
        /// it input. Only full-body skills block; upper-body skills merely slow the actor.
        /// </summary>
        /// <remarks>
        /// A staged melee skill blocks only on its <see cref="StageLocomotion.Locked"/> stages: a
        /// <see cref="StageLocomotion.Controlled"/> opener deliberately leaves the controller
        /// driving. Skills without stages are unaffected — their synthesized stage is locked, so
        /// they block for their whole duration exactly as before.
        /// </remarks>
        public bool BlocksMovement => ActiveStageLocomotion == StageLocomotion.Locked;

        /// <inheritdoc />
        /// <remarks>
        /// A staged melee skill suppresses on any stage that has not opted into root motion,
        /// whatever its body domain: stage movement is code-driven by the possessing controller, so
        /// forwarding the base layer's locomotion root motion at the same time would double-move the
        /// actor. Non-staged skills keep the original rule — full body suppresses unless the skill
        /// opted in (a lunge is expected to carry the actor), upper body never suppresses so a cast
        /// keeps walking on root motion. Re-evaluated every stage change rather than once per skill.
        /// </remarks>
        public bool IsSuppressingRootMotion {
            get {
                if (ActiveStage != null) return !UsesRootMotion();

                return IsFullBodySkillActive() && !UsesRootMotion();
            }
        }

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

        /// <summary>
        /// Raised when a combo stage begins, carrying the running skill and the stage's index. Fired
        /// for the opening stage too, immediately after <see cref="OnSkillStarted"/>, so a listener
        /// that only handles stages sees every swing without special-casing the first.
        /// </summary>
        /// <remarks>
        /// Raised when the stage's animator trigger is set, not when its clip is observed starting,
        /// so a listener that reads the animator will still see the outgoing state for a frame or
        /// two. Only staged melee skills raise it; a projectile or upper-body skill never does.
        /// </remarks>
        public event Action<SkillDefinition, int> OnSkillStageStarted;

        /// <summary>Raised for each melee hit that lands, after the injury has been applied.</summary>
        public event Action<SkillDefinition, HurtBox> OnSkillHit;

        /// <summary>Raised when a skill ends, whether it landed, missed, or was cancelled.</summary>
        public event Action<SkillDefinition> OnSkillFinished;

        private readonly List<SkillInstance> _skills = new();
        private readonly List<SkillInstance> _slots = new();
        private readonly Dictionary<string, Transform> _spawnBoneCache = new();
        private readonly Dictionary<MeleeSkillDefinition, MeleeComboStage[]> _implicitStageCache = new();

        private Actor _actor;
        private ResourceSet _resources;
        private StaggerSystem _stagger;
        private SkillInstance _active;

        private bool _enteredSkillState;
        private bool _hitBoxActive;
        private bool _damageWindowSpent;
        private bool _hasLanded;
        private int _projectilesFired;
        private float _rotationUsed;
        private Quaternion _lastRotation;
        private float _upperBodyWeight;

        private MeleeComboStage[] _runtimeStages;
        private int _stageIndex = -1;
        private bool _bufferedAdvance;
        private bool _stageEntered;
        private float _stageEntryWait;
        private int _stageStateHash;
        private int _previousStageStateHash;
        private float _lastStageNormalizedTime;

        protected override void Start() {
            base.Start();

            _actor = GetComponent<Actor>();
            _resources = GetComponent<ResourceSet>();
            _stagger = GetComponent<StaggerSystem>();

            if (_stagger != null)
                _stagger.OnStaggerStarted += CancelActive;

            if (hitBox != null)
                hitBox.Init(this);

            EnsureUpperBodyAim();
        }

        /// <remarks>
        /// Releases the stagger subscription before the base implementation drops the owner-death
        /// one. An actor destroyed mid-combo would otherwise leave the stagger system holding a
        /// reference to a dead component.
        /// </remarks>
        protected override void OnDestroy() {
            if (_stagger != null)
                _stagger.OnStaggerStarted -= CancelActive;

            base.OnDestroy();
        }

        /// <summary>
        /// Adds an <see cref="UpperBodyAim"/> to the animator's GameObject when none is present, so
        /// upper-body skills aim where the actor faces without per-prefab wiring. Hand-place the
        /// component on the animator to override its default weights.
        /// </summary>
        private void EnsureUpperBodyAim() {
            if (_actor == null || _actor.Animator == null) return;
            if (_actor.Animator.GetComponent<UpperBodyAim>() != null) return;

            _actor.Animator.gameObject.AddComponent<UpperBodyAim>();
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
        /// Uses whatever is in a hotbar slot, or continues it when that skill is the combo already
        /// running.
        /// </summary>
        /// <returns>
        /// True when the skill started or a combo advance was buffered; false for an empty slot or
        /// any failed guard.
        /// </returns>
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
        /// True when the skill started, or when it is the combo already running and the press was
        /// buffered as an advance to its next stage. False when the actor is dead, a
        /// <em>different</em> skill is already running, this one is still on cooldown, or its costs
        /// cannot be paid.
        /// </returns>
        public bool TryUse(SkillDefinition skill) {
            if (skill == null) return false;

            return TryUseInstance(ResolveOrGrant(skill));
        }

        /// <summary>
        /// Interrupts the running skill, if any, breaking a combo wherever it had got to. The
        /// cooldown still applies, spent resources are not refunded, and the skill's animator
        /// trigger — along with every one of its stage triggers — is reset so a queued re-entry
        /// cannot fire it again.
        /// </summary>
        /// <remarks>
        /// This is what a stagger uses to take an actor out of its swing: <see cref="StaggerSystem"/>
        /// raises its start event before it pins the actor, so the resume this performs cannot undo
        /// the stagger's own suppression.
        /// </remarks>
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
        /// matching <see cref="CombatSystem.OnHitBoxContact"/>: one swing is one hit. For a combo
        /// that is one hit per stage — advancing the chain opens a fresh window — and the landing
        /// stage's overrides are what the packet carries.
        /// </remarks>
        public void OnHitBoxContact(HurtBox hurtBox) {
            if (_active == null) return;
            if (hurtBox == null) return;
            if (hurtBox.Owner.transform.root == transform.root) return;

            var skill = _active.Definition;
            var stage = ActiveStage;
            var packet = stage != null ? BuildDamagePacket(skill, stage) : BuildDamagePacket(skill);

            DamageResolver.Apply(packet, hurtBox);
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

            bool isFullBody = _active.Definition.bodyDomain == SkillBodyDomain.FullBody;

            if (_runtimeStages != null) {
                TickStagedMeleeSkill(
                    isFullBody ? fullBodyLayerIndex : upperBodyLayerIndex,
                    isFullBody ? FullBodyStateTag : UpperBodyStateTag);
                return;
            }

            if (isFullBody) {
                TickAnimatorDrivenSkill(fullBodyLayerIndex, FullBodyStateTag);
                return;
            }

            TickAnimatorDrivenSkill(upperBodyLayerIndex, UpperBodyStateTag);
        }

        /// <summary>
        /// Drives a staged melee chain on the layer its body domain plays on: waits for each stage's
        /// clip to actually take over that layer, runs that stage's damage window, and hands off to
        /// the next stage when a buffered press comes due.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This replaces the entered/left latch <see cref="TickAnimatorDrivenSkill"/> uses, because
        /// that latch cannot tell one swing from the next: consecutive stages carry the same tag, so
        /// "still in a tagged state" is true across the whole chain. Stages are separated by
        /// identity instead — the state's <c>fullPathHash</c> — and the skill ends the moment the
        /// layer leaves the state the current stage claimed. An upper-body chain works exactly the
        /// same way through "UpperSkill"-tagged states on the upper-body layer, with the base layer
        /// left free to keep animating locomotion.
        /// </para>
        /// <para>
        /// Hence the wait branch. During the crossfade into the next stage,
        /// <c>GetCurrentAnimatorStateInfo</c> still reports the <em>outgoing</em> state, so a stage
        /// that accepted the first Attack-tagged state it saw would immediately claim the state it
        /// is leaving and then finish the skill as soon as the crossfade completed. Waiting until the
        /// reported state differs from the previous stage's is what lets the transition land. For the
        /// opening stage the remembered previous state is zero, so any Attack-tagged state is
        /// accepted — including one that was somehow already live, which is acceptable because the
        /// skill's own trigger was just fired at it.
        /// </para>
        /// </remarks>
        private void TickStagedMeleeSkill(int layerIndex, string stateTag) {
            var stateInfo = _actor.Animator.GetCurrentAnimatorStateInfo(layerIndex);
            bool inSkillState = stateInfo.IsTag(stateTag);

            if (!_stageEntered && !TryClaimStageState(stateInfo, inSkillState)) return;

            if (!inSkillState || stateInfo.fullPathHash != _stageStateHash) {
                FinishSkill();
                return;
            }

            float normalizedTime = stateInfo.normalizedTime;
            _lastStageNormalizedTime = normalizedTime % 1f;

            TickMeleeWindow(ActiveStage, _lastStageNormalizedTime);

            if (_bufferedAdvance && HasNextStage() && normalizedTime >= ActiveStage.comboAdvanceTime) {
                EnterStage(_stageIndex + 1);
                return;
            }

            if (normalizedTime >= 1f)
                FinishSkill();
        }

        /// <summary>
        /// Binds the current stage to the animator state now playing, once that state carries the
        /// chain's tag and is no longer the one the previous stage was in.
        /// </summary>
        /// <returns>True once the stage owns a state; false while still waiting for the transition.</returns>
        /// <remarks>
        /// A stage whose trigger reaches no such state would hang the whole skill bar, so the wait is
        /// bounded by <see cref="StageEntryTimeout"/> and cancels the skill rather than deadlocking
        /// it. The timeout is generous on purpose: it is a wiring-error backstop, not a gameplay
        /// timer.
        /// </remarks>
        private bool TryClaimStageState(AnimatorStateInfo stateInfo, bool inSkillState) {
            if (inSkillState && stateInfo.fullPathHash != _previousStageStateHash) {
                _stageEntered = true;
                _stageStateHash = stateInfo.fullPathHash;
                return true;
            }

            _stageEntryWait += Time.deltaTime;
            if (_stageEntryWait > StageEntryTimeout)
                CancelActive();

            return false;
        }

        private bool HasNextStage() {
            return _runtimeStages != null && _stageIndex + 1 < _runtimeStages.Length;
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
            if (_active.Definition is MeleeSkillDefinition) {
                TickMeleeWindow(ActiveStage, normalizedTime);
                return;
            }

            if (_active.Definition is ProjectileSkillDefinition projectileSkill)
                TickProjectileVolley(projectileSkill, normalizedTime);
        }

        /// <remarks>
        /// Timings come from the stage rather than the definition, so every swing of a combo has its
        /// own live frames. An upper-body melee skill runs its synthesized or first stage here too —
        /// it is ticked by <see cref="TickAnimatorDrivenSkill"/> and so never advances past stage
        /// zero, because combo chaining is a full-body affair.
        /// </remarks>
        private void TickMeleeWindow(MeleeComboStage stage, float normalizedTime) {
            if (hitBox == null) return;
            if (stage == null) return;

            if (_hitBoxActive && normalizedTime >= stage.damageWindowEnd) {
                CloseDamageWindow();
                return;
            }

            if (_hitBoxActive || _damageWindowSpent || _hasLanded) return;
            if (normalizedTime < stage.damageWindowStart) return;
            if (normalizedTime >= stage.damageWindowEnd) return;

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
            Vector3 origin = ResolveProjectileOrigin(skill);
            Vector3 direction = ProjectilePatterns.GetDirection(skill.pattern, index, count, transform.forward, Vector3.up, skill.spreadAngle);

            var projectile = Instantiate(skill.projectilePrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
            projectile.Launch(origin, direction, skill.projectileSpeed, skill.projectileLifetime, BuildDamagePacket(skill), gameObject);
        }

        /// <summary>
        /// Position shots leave from: the skill's named bone when set and found, else the serialized
        /// origin transform, else a metre above the actor's feet.
        /// </summary>
        /// <remarks>
        /// Bone lookups are cached by name after the first search, so per-shot cost is a dictionary
        /// hit. A named bone that does not exist under the animator falls back silently — the skill
        /// still fires, just from the generic origin — because a rig swap should degrade aim polish,
        /// not break the skill.
        /// </remarks>
        private Vector3 ResolveProjectileOrigin(ProjectileSkillDefinition skill) {
            Transform bone = ResolveSpawnBone(skill.spawnBoneName);
            if (bone != null) return bone.position;
            if (projectileOrigin != null) return projectileOrigin.position;

            return transform.position + Vector3.up;
        }

        private Transform ResolveSpawnBone(string boneName) {
            if (string.IsNullOrEmpty(boneName)) return null;
            if (_actor == null || _actor.Animator == null) return null;
            if (_spawnBoneCache.TryGetValue(boneName, out Transform cached)) return cached;

            Transform bone = FindDescendant(_actor.Animator.transform, boneName);
            _spawnBoneCache[boneName] = bone;

            return bone;
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

        /// <remarks>
        /// Damage is resolved at the moment the hit or the launch happens, not when the skill starts,
        /// so a buff that lands mid-animation is counted. The skill's tags are the query context, so
        /// only modifiers whose own tags are a subset of them contribute.
        /// </remarks>
        private DamagePacket BuildDamagePacket(SkillDefinition skill) {
            return new DamagePacket(ResolveDamage(skill), skill.tags, skill.injury, skill.injurySeverity, gameObject);
        }

        /// <summary>
        /// Builds the packet for one swing of a combo, layering the stage's overrides over the
        /// skill's resolved payload.
        /// </summary>
        /// <remarks>
        /// The stage's multiplier is applied <em>after</em> stat scaling, so "the finisher hits twice
        /// as hard" stays true regardless of how much damage the actor's stats added. Overrides use
        /// the stage's inherit sentinels — a null injury and a negative severity both fall through to
        /// the skill — so an unauthored stage cannot silently strip the skill's wound.
        /// </remarks>
        private DamagePacket BuildDamagePacket(SkillDefinition skill, MeleeComboStage stage) {
            float amount = ResolveDamage(skill) * stage.damageMultiplier;
            var injury = stage.injuryOverride != null ? stage.injuryOverride : skill.injury;
            float severity = stage.injurySeverityOverride >= 0f ? stage.injurySeverityOverride : skill.injurySeverity;

            return new DamagePacket(amount, skill.tags, injury, severity, gameObject);
        }

        private float ResolveDamage(SkillDefinition skill) {
            float weaponDamage = skill.addWeaponDamage ? WeaponDamageProvider?.Invoke(skill) ?? 0f : 0f;
            float baseDamage = skill.baseDamage + weaponDamage;

            return damageStat != null ? _actor.Stats.Evaluate(damageStat, skill.tags, baseDamage) : baseDamage;
        }

        /// <remarks>
        /// Re-using the skill that is already running is not a failed use but a combo input: it
        /// buffers an advance to the next stage instead of being rejected. Using a
        /// <em>different</em> skill mid-animation is still refused.
        /// </remarks>
        private bool TryUseInstance(SkillInstance instance) {
            if (Owner == null || !Owner.IsAlive) return false;
            if (instance == _active) return TryBufferComboAdvance();
            if (IsUsingSkill) return false;
            if (!instance.IsReady) return false;
            if (!TrySpendCosts(instance.Definition)) return false;

            StartSkill(instance);
            return true;
        }

        /// <summary>
        /// Records a request to advance the running combo, honoured at the current stage's
        /// <see cref="MeleeComboStage.comboAdvanceTime"/>.
        /// </summary>
        /// <returns>
        /// True when the press was buffered. False when the skill has no further stage, has not yet
        /// reached its stage's clip, or is still before the stage's
        /// <see cref="MeleeComboStage.comboWindowStart"/> — which is what stops a mashed button from
        /// queueing the whole chain off the opening frame.
        /// </returns>
        /// <remarks>
        /// A buffered advance costs nothing: the chain's resources are billed once, when the skill
        /// starts, and its cooldown begins once, when the last stage ends. Continuing a combo is
        /// therefore never blocked by affordability, which is deliberate — running out of stamina
        /// mid-swing should end the chain at the swing boundary the player already paid for, not
        /// strand them in a stage they cannot leave.
        /// </remarks>
        private bool TryBufferComboAdvance() {
            if (_runtimeStages == null) return false;
            if (_stageIndex + 1 >= _runtimeStages.Length) return false;
            if (!_stageEntered) return false;
            if (_lastStageNormalizedTime < ActiveStage.comboWindowStart) return false;

            _bufferedAdvance = true;
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

        /// <remarks>
        /// A melee skill hands its animator trigger to <see cref="EnterStage"/> instead of firing it
        /// here, so the opening stage goes through exactly the path every later stage does. That
        /// puts the trigger — and <see cref="OnSkillStageStarted"/> — after
        /// <see cref="OnSkillStarted"/>, which is immaterial to the animator (it is not evaluated
        /// until the next update) and gives listeners a fixed event order to rely on.
        /// </remarks>
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

            ClearStageState();
            _runtimeStages = ResolveRuntimeStages(skill);

            if (skill.bodyDomain == SkillBodyDomain.FullBody)
                _actor.SuppressLocomotion();
            else
                ApplyCastSlow(skill);

            if (skill.noiseRadius > 0f)
                NoiseEmitter.Emit(gameObject, transform.position, skill.noiseRadius);

            if (_runtimeStages == null && _actor.Animator != null && !string.IsNullOrEmpty(skill.animationTrigger))
                _actor.Animator.SetTrigger(skill.animationTrigger);

            OnSkillStarted?.Invoke(skill);

            if (_runtimeStages != null)
                EnterStage(0);
        }

        /// <summary>
        /// The stage chain a skill runs as: its authored stages, a one-entry chain synthesized from
        /// its own fields when it is a melee skill without stages, or null when it is not a melee
        /// skill at all and therefore has no stages.
        /// </summary>
        /// <remarks>
        /// Synthesized chains are cached per definition, so a legacy melee skill allocates one array
        /// the first time it is used and none after that. The cache is keyed by asset reference and
        /// never invalidated: editing <see cref="MeleeSkillDefinition.damageWindowStart"/> on a live
        /// asset in play mode will not be picked up until the next play session.
        /// </remarks>
        private MeleeComboStage[] ResolveRuntimeStages(SkillDefinition skill) {
            if (skill is not MeleeSkillDefinition melee) return null;
            if (melee.stages != null && melee.stages.Length > 0) return melee.stages;
            if (_implicitStageCache.TryGetValue(melee, out MeleeComboStage[] cached)) return cached;

            var synthesized = new[] { BuildImplicitStage(melee) };
            _implicitStageCache[melee] = synthesized;

            return synthesized;
        }

        /// <remarks>
        /// The synthesized stage is what keeps unstaged melee skills behaving exactly as they did
        /// before combos existed: locked locomotion and the definition's own root motion flag, so
        /// <see cref="BlocksMovement"/> and <see cref="IsSuppressingRootMotion"/> answer as before,
        /// and a frozen turn cap, because nothing used to steer a swing. Its combo fields are never
        /// read — a one-entry chain has no next stage to advance to.
        /// </remarks>
        private static MeleeComboStage BuildImplicitStage(MeleeSkillDefinition skill) {
            return new MeleeComboStage {
                animationTrigger = skill.animationTrigger,
                damageWindowStart = skill.damageWindowStart,
                damageWindowEnd = skill.damageWindowEnd,
                locomotion = StageLocomotion.Locked,
                useRootMotion = skill.useRootMotion,
                turnSpeedCap = 0f
            };
        }

        /// <summary>
        /// Starts one stage of the running combo: closes whatever the previous stage left open,
        /// resets the per-swing state, fires the stage's animator trigger and announces it.
        /// </summary>
        /// <param name="index">Index into the active skill's stage chain. Assumed in range.</param>
        /// <remarks>
        /// The rotation budget and the landed/spent latches are per stage, not per skill: each swing
        /// gets its own hit and its own <see cref="SkillDefinition.maxRotation"/> allowance,
        /// otherwise a three-hit combo would land once and then be unable to track its target.
        /// </remarks>
        private void EnterStage(int index) {
            CloseDamageWindow();

            var stage = _runtimeStages[index];

            _previousStageStateHash = _stageStateHash;
            _stageIndex = index;
            _stageEntered = false;
            _stageEntryWait = 0f;
            _bufferedAdvance = false;
            _damageWindowSpent = false;
            _hasLanded = false;
            _lastStageNormalizedTime = 0f;
            _rotationUsed = 0f;
            _lastRotation = transform.rotation;

            if (_actor.Animator != null && !string.IsNullOrEmpty(stage.animationTrigger))
                _actor.Animator.SetTrigger(stage.animationTrigger);

            OnSkillStageStarted?.Invoke(_active.Definition, index);
        }

        /// <remarks>
        /// The cooldown belongs to the whole chain, not to each swing: it starts here, when the last
        /// stage ends or the combo is broken, so a three-hit combo is one use of the skill. Every
        /// stage trigger is cleared on the way out — a trigger the animator never consumed would
        /// otherwise still be latched when the chain is next started and skip it straight past its
        /// opener.
        /// </remarks>
        private void FinishSkill() {
            CloseDamageWindow();
            ResetStageTriggers();

            var skill = _active.Definition;

            if (skill.bodyDomain == SkillBodyDomain.FullBody)
                _actor.ResumeLocomotion();
            else
                _actor.Stats.RemoveModifiersFrom(this);

            _active.CooldownRemaining = skill.cooldown;
            _active = null;
            ClearStageState();

            OnSkillFinished?.Invoke(skill);
        }

        private void ResetStageTriggers() {
            if (_actor.Animator == null) return;
            if (_runtimeStages == null) return;

            foreach (var stage in _runtimeStages) {
                if (stage == null || string.IsNullOrEmpty(stage.animationTrigger)) continue;

                _actor.Animator.ResetTrigger(stage.animationTrigger);
            }
        }

        private void ClearStageState() {
            _runtimeStages = null;
            _stageIndex = -1;
            _stageEntered = false;
            _stageEntryWait = 0f;
            _stageStateHash = 0;
            _previousStageStateHash = 0;
            _bufferedAdvance = false;
            _lastStageNormalizedTime = 0f;
        }

        private bool IsStageIndexValid() {
            return _runtimeStages != null && _stageIndex >= 0 && _stageIndex < _runtimeStages.Length;
        }

        private bool IsFullBodySkillActive() {
            return _active != null && _active.Definition.bodyDomain == SkillBodyDomain.FullBody;
        }

        /// <remarks>
        /// The stage's flag wins when there is one, which for a melee skill is always — an unstaged
        /// melee skill's synthesized stage copies <see cref="SkillDefinition.useRootMotion"/>, so the
        /// answer is unchanged. Only non-melee full-body skills fall through to the definition.
        /// </remarks>
        private bool UsesRootMotion() {
            if (_active == null) return false;

            var stage = ActiveStage;
            return stage != null ? stage.useRootMotion : _active.Definition.useRootMotion;
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
