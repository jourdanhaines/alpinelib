using System;
using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// Drives the actor's capsule between a standing and a crouched height, and refuses to stand back
    /// up while something is directly overhead.
    /// </summary>
    /// <remarks>
    /// This owns capsule geometry only. It deliberately does not touch move speed or noise — that is
    /// <see cref="LocomotionSystem"/>'s job through the crouch gaits — so a controller crouching an actor
    /// calls both, and either can be used without the other (a cutscene can shrink a capsule without
    /// slowing anything, and a stealth game can use the crouch gaits on an actor whose capsule never
    /// changes).
    ///
    /// Standing is a request, not a command. <see cref="SetCrouching"/> with <c>false</c> under a low
    /// ceiling latches <see cref="WantsToStand"/> and stands the actor the first frame the ceiling clears,
    /// which is what makes a crouch tunnel feel right: the player releases crouch part way through, keeps
    /// crawling, and pops up on their own the moment they are out. The alternative — rejecting the
    /// request outright — forces the player to keep tapping crouch at the exit.
    ///
    /// Extends <see cref="ActorSubsystem"/> for the standard death behaviour: the base disables this
    /// component when the owner dies, freezing the capsule at whatever height it had. That is correct
    /// here — <see cref="Actor.Kill"/> switches collision off anyway, and a corpse resizing its capsule
    /// would only fight whatever ragdoll or death animation takes over.
    ///
    /// The capsule itself belongs to the actor's motor; this asks it to resize and to check headroom, so
    /// the probe uses the motor's own mask and skin and can never hit the actor's own collider.
    ///
    /// Crouch state is mirrored into the animator's <c>Crouching</c> bool whenever the actor's
    /// controller declares it, following the same opt-in convention as the actor's <c>Grounded</c>
    /// and strafe parameters: controllers without crouch locomotion are never written to. The write
    /// happens on state change rather than per frame because <see cref="IsCrouching"/> is edge-driven
    /// — there is nothing to re-derive between changes.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class CrouchSystem : ActorSubsystem {
        [Header("Heights")]
        [Tooltip("Capsule height in metres while standing. Should match the capsule height authored on the prefab.")]
        [SerializeField] private float standingHeight = 1.8f;
        [Tooltip("Capsule height in metres while crouched.")]
        [SerializeField] private float crouchHeight = 0.9f;

        [Header("Transition")]
        [Tooltip("How fast the capsule approaches its target height. Higher is snappier; this is an exponential approach rate, not metres per second.")]
        [SerializeField] private float transitionSpeed = 8f;

        /// <summary>
        /// True while the actor is crouched or transitioning into a crouch.
        /// </summary>
        /// <remarks>
        /// Flips the instant the request is accepted rather than when the capsule finishes shrinking, so
        /// gameplay reading it — gait selection, jump blocking, camera height — reacts on the same frame
        /// as the input instead of lagging the transition.
        /// </remarks>
        public bool IsCrouching { get; private set; }

        /// <summary>
        /// True while a stand request is being held back by a ceiling. Cleared as soon as the actor
        /// stands, or if it is crouched again before the ceiling clears.
        /// </summary>
        public bool WantsToStand { get; private set; }

        /// <summary>
        /// Raised whenever <see cref="IsCrouching"/> changes, with the new value.
        /// </summary>
        /// <remarks>
        /// An event rather than a polled flag so first-person camera height, body visibility and audio can
        /// react without every one of them running an <c>Update</c> to compare against last frame.
        /// </remarks>
        public event Action<bool> OnCrouchChanged;

        /// <summary>
        /// Height difference in metres below which the capsule is snapped to its target, ending the
        /// exponential approach that would otherwise never quite arrive.
        /// </summary>
        private const float HeightSnapEpsilon = 0.001f;

        /// <summary>
        /// Animator bool mirroring <see cref="IsCrouching"/>, written only when the controller
        /// declares it.
        /// </summary>
        private const string CrouchingParameter = "Crouching";

        private Actor _actor;
        private Animator _animator;
        private int _crouchingParameterHash;
        private bool _hasCrouchingParameter;

        protected override void Start() {
            base.Start();

            _actor = GetComponent<Actor>();
            _animator = _actor.Animator;
            _crouchingParameterHash = Animator.StringToHash(CrouchingParameter);
            _hasCrouchingParameter = DeclaresCrouchingParameter();
            WriteCrouchingParameter();
        }

        /// <summary>
        /// Requests a crouch or a stand. Crouching always takes effect immediately; standing is deferred
        /// while <see cref="CanStand"/> is false and happens automatically once the ceiling clears.
        /// </summary>
        /// <param name="crouching">True to crouch, false to stand.</param>
        public void SetCrouching(bool crouching) {
            if (crouching) {
                WantsToStand = false;
                ApplyCrouchState(true);
                return;
            }

            if (!CanStand()) {
                WantsToStand = true;
                return;
            }

            WantsToStand = false;
            ApplyCrouchState(false);
        }

        /// <summary>
        /// Reports whether there is room above the actor to return to <c>standingHeight</c>.
        /// </summary>
        /// <returns>True when standing is clear, or when the actor is already standing.</returns>
        public bool CanStand() {
            if (_actor == null) return true;
            if (standingHeight - _actor.CapsuleHeight <= 0f) return true;

            return _actor.HasHeadroom(standingHeight);
        }

        private void Update() {
            if (WantsToStand) {
                TryDeferredStand();
            }

            ApplyHeight();
        }

        /// <summary>
        /// Stands the actor up as soon as a deferred stand request becomes legal.
        /// </summary>
        private void TryDeferredStand() {
            if (!CanStand()) return;

            WantsToStand = false;
            ApplyCrouchState(false);
        }

        /// <summary>
        /// Moves the capsule one frame closer to its target height; the actor keeps its feet planted and
        /// moves the top.
        /// </summary>
        /// <remarks>
        /// The blend is framerate independent — <c>1 - e^(-rate * dt)</c> rather than <c>rate * dt</c> —
        /// so the same <c>transitionSpeed</c> produces the same curve at 60 and 144 fps. The result is
        /// snapped once it is within a millimetre because an exponential approach is asymptotic, and a
        /// capsule that is forever 0.4 mm short of standing height would make <see cref="CanStand"/>
        /// keep probing a hair-thin gap every frame.
        /// </remarks>
        private void ApplyHeight() {
            if (_actor == null) return;

            float currentHeight = _actor.CapsuleHeight;
            float targetHeight = IsCrouching ? crouchHeight : standingHeight;
            if (Mathf.Approximately(currentHeight, targetHeight)) return;

            float blend = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);
            float nextHeight = Mathf.Lerp(currentHeight, targetHeight, blend);

            if (Mathf.Abs(nextHeight - targetHeight) < HeightSnapEpsilon) {
                nextHeight = targetHeight;
            }

            _actor.SetCapsuleHeight(nextHeight);
        }

        /// <summary>
        /// Commits a crouch state change and raises <see cref="OnCrouchChanged"/>, ignoring no-op writes.
        /// </summary>
        private void ApplyCrouchState(bool crouching) {
            if (IsCrouching == crouching) return;

            IsCrouching = crouching;
            WriteCrouchingParameter();
            OnCrouchChanged?.Invoke(crouching);
        }

        private void WriteCrouchingParameter() {
            if (!_hasCrouchingParameter) return;

            _animator.SetBool(_crouchingParameterHash, IsCrouching);
        }

        /// <remarks>
        /// Resolved once, in <c>Start</c>, because <see cref="Animator.parameters"/> allocates on
        /// every access — same convention as the actor's optional parameter scans.
        /// </remarks>
        private bool DeclaresCrouchingParameter() {
            if (_animator == null) return false;
            if (_animator.runtimeAnimatorController == null) return false;

            foreach (AnimatorControllerParameter parameter in _animator.parameters) {
                if (parameter.name == CrouchingParameter) return true;
            }

            return false;
        }
    }
}
