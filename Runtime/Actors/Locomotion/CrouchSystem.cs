using System;
using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// Drives the actor's <see cref="CharacterController"/> capsule between a standing and a crouched
    /// height, and refuses to stand back up while something is directly overhead.
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
    /// here — <see cref="Actor.Kill"/> switches the controller off anyway, and a corpse resizing its
    /// capsule would only fight whatever ragdoll or death animation takes over.
    ///
    /// The <see cref="CharacterController"/> requirement is declared here as well, even though
    /// <see cref="Actor"/> already implies it: the capsule is the one thing this component exists to
    /// resize, and relying on another component's requirement to guarantee it leaves a per-frame null
    /// dereference waiting on any object where that chain is broken.
    ///
    /// Crouch state is mirrored into the animator's <c>Crouching</c> bool whenever the actor's
    /// controller declares it, following the same opt-in convention as the actor's <c>Grounded</c>
    /// and strafe parameters: controllers without crouch locomotion are never written to. The write
    /// happens on state change rather than per frame because <see cref="IsCrouching"/> is edge-driven
    /// — there is nothing to re-derive between changes.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    [RequireComponent(typeof(CharacterController))]
    public class CrouchSystem : ActorSubsystem {
        [Header("Heights")]
        [Tooltip("Capsule height in metres while standing. Should match the CharacterController height authored on the prefab.")]
        [SerializeField] private float standingHeight = 1.8f;
        [Tooltip("Capsule height in metres while crouched.")]
        [SerializeField] private float crouchHeight = 0.9f;

        [Header("Transition")]
        [Tooltip("How fast the capsule approaches its target height. Higher is snappier; this is an exponential approach rate, not metres per second.")]
        [SerializeField] private float transitionSpeed = 8f;

        [Header("Ceiling Check")]
        [Tooltip("Layers that can block standing up. Should exclude the actor's own layer so the check cannot hit the actor's own colliders.")]
        [SerializeField] private LayerMask ceilingMask = Physics.DefaultRaycastLayers;

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
        /// Metres subtracted from the controller radius to size the ceiling probe, so the probe cannot
        /// graze the actor's own capsule or a wall it is already flush against.
        /// </summary>
        /// <remarks>
        /// An absolute clearance rather than a fraction of the radius: the gap that has to be cleared is
        /// the collision skin and the wall the actor is leaning on, both of which are measured in metres
        /// and neither of which grows with the width of the actor.
        /// </remarks>
        private const float ProbeRadiusClearance = 0.05f;

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

        private CharacterController _controller;
        private Animator _animator;
        private int _crouchingParameterHash;
        private bool _hasCrouchingParameter;

        protected override void Start() {
            base.Start();

            _controller = GetComponent<CharacterController>();
            _animator = GetComponent<Actor>().Animator;
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
        /// <remarks>
        /// Sweeps a sphere upward from the capsule's top hemisphere centre across exactly the height the
        /// actor is missing, using a slightly shrunk controller radius. The shrink matters twice: it keeps
        /// the probe from starting inside the actor's own collider, and it stops an actor pressed against
        /// a wall from reading that wall as a ceiling. The mask is still the real defence against
        /// self-hits — <c>ceilingMask</c> should not include the actor's own layer.
        ///
        /// The origin is built from <c>transform.position + controller.center</c> rather than
        /// <c>TransformPoint</c> because a character controller capsule is world-axis-aligned regardless
        /// of the transform's yaw; going through the transform would only introduce scale artefacts.
        /// </remarks>
        public bool CanStand() {
            if (_controller == null) return true;

            float missingHeight = standingHeight - _controller.height;
            if (missingHeight <= 0f) return true;

            float probeRadius = Mathf.Max(_controller.radius - ProbeRadiusClearance, 0.01f);
            Vector3 capsuleTop = transform.position + _controller.center + Vector3.up * (_controller.height * 0.5f - probeRadius);

            return !Physics.SphereCast(
                capsuleTop, probeRadius, Vector3.up, out _, missingHeight, ceilingMask, QueryTriggerInteraction.Ignore
            );
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
        /// Moves the capsule one frame closer to its target height and keeps its centre at half that
        /// height, so the actor's feet stay planted while the top of the capsule moves.
        /// </summary>
        /// <remarks>
        /// The blend is framerate independent — <c>1 - e^(-rate * dt)</c> rather than <c>rate * dt</c> —
        /// so the same <c>transitionSpeed</c> produces the same curve at 60 and 144 fps. The result is
        /// snapped once it is within a millimetre because an exponential approach is asymptotic, and a
        /// capsule that is forever 0.4 mm short of standing height would make <see cref="CanStand"/>
        /// keep casting a hair-thin probe every frame.
        ///
        /// The controller is guarded the same way <see cref="CanStand"/> guards it, so an actor whose
        /// capsule has been removed at runtime degrades to doing nothing instead of throwing once per
        /// frame for the rest of the session.
        /// </remarks>
        private void ApplyHeight() {
            if (_controller == null) return;

            float targetHeight = IsCrouching ? crouchHeight : standingHeight;
            if (Mathf.Approximately(_controller.height, targetHeight)) return;

            float blend = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);
            float nextHeight = Mathf.Lerp(_controller.height, targetHeight, blend);

            if (Mathf.Abs(nextHeight - targetHeight) < HeightSnapEpsilon) {
                nextHeight = targetHeight;
            }

            _controller.height = nextHeight;
            _controller.center = Vector3.up * (nextHeight * 0.5f);
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
