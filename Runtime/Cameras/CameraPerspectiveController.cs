using System;
using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// Owns the single camera shared by a first- and a third-person rig, and slides it between the two
    /// when the player switches perspective. Both rigs run all the time; this only decides which one's
    /// anchor the camera is sitting on, and interpolates the camera between anchors for the length of a
    /// blend.
    /// </summary>
    /// <remarks>
    /// The camera is driven from here rather than parented to a rig anchor because a parented camera can
    /// only ever be in one rig's hierarchy, which makes a cross-rig blend impossible: the camera has to
    /// exist between the two poses for a quarter of a second. Rigs therefore publish poses through
    /// <see cref="ICameraRig.CameraAnchor"/> and never touch a camera themselves.
    ///
    /// The execution order pushes this after the rigs' own <c>LateUpdate</c> (they run at the default
    /// zero), so the anchors read here are this frame's poses rather than last frame's — otherwise the
    /// camera trails the rig by a frame, which is visible as swim during fast look input.
    ///
    /// Switching copies the outgoing rig's yaw and pitch onto the incoming one so aim is continuous
    /// across the change, and <see cref="OnPerspectiveChanged"/> fires at the *start* of the blend
    /// rather than the end: games hide or show the player's body on that event, and the body has to be
    /// gone before the camera travels inside it, not after.
    /// </remarks>
    [DefaultExecutionOrder(200)]
    public class CameraPerspectiveController : MonoBehaviour {
        [Header("Rigs")]
        [SerializeField] private FirstPersonCameraRig firstPersonRig;
        [SerializeField] private ThirdPersonCameraRig thirdPersonRig;

        [Tooltip("Camera moved between the two rigs' anchors. Should not be parented to either rig.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Blend")]
        [Tooltip("Seconds the camera takes to travel between rig anchors. Zero switches instantly.")]
        [SerializeField] private float blendDuration = 0.25f;

        [Tooltip("Shapes the travel across the blend. X is normalised time, Y is progress towards the incoming anchor.")]
        [SerializeField] private AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Startup")]
        [Tooltip("Perspective the camera starts on. No OnPerspectiveChanged is raised for it.")]
        [SerializeField] private CameraPerspective startingPerspective = CameraPerspective.ThirdPerson;

        /// <summary>
        /// Perspective currently selected. Updates the moment a switch starts, not when its blend ends.
        /// </summary>
        public CameraPerspective Perspective { get; private set; }

        /// <summary>
        /// Rig for the current <see cref="Perspective"/>, or null when that rig is unassigned. Feed look
        /// input and read planar axes through this rather than through a rig reference of your own.
        /// </summary>
        public ICameraRig ActiveRig => ResolveRig(Perspective);

        /// <summary>
        /// True while the camera is still travelling between the two rigs' anchors.
        /// </summary>
        public bool IsBlending => _isBlending;

        /// <summary>
        /// Raised with the incoming perspective the moment a switch begins, before the camera has moved.
        /// </summary>
        /// <remarks>
        /// Not raised for <see cref="Perspective"/>'s starting value, because subscribers that hook this
        /// up in their own <c>Start</c> would miss it and end up disagreeing with the camera. Read
        /// <see cref="Perspective"/> once on startup to seed state, then follow this event.
        /// </remarks>
        public event Action<CameraPerspective> OnPerspectiveChanged;

        private bool _isBlending;
        private float _blendElapsed;
        private Vector3 _blendStartPosition;
        private Quaternion _blendStartRotation;

        private Transform ActiveAnchor => ActiveRig?.CameraAnchor;

        private void Awake() {
            Perspective = startingPerspective;
        }

        /// <remarks>
        /// The first pin happens in <c>Start</c> rather than <c>Awake</c> so the rigs have resolved their
        /// anchors — they do that in their own <c>Awake</c> — before one is read.
        /// </remarks>
        private void Start() {
            PinToActiveAnchor();
        }

        /// <summary>
        /// Flips to the other perspective, blending across.
        /// </summary>
        public void Toggle() {
            SetPerspective(
                Perspective == CameraPerspective.FirstPerson
                    ? CameraPerspective.ThirdPerson
                    : CameraPerspective.FirstPerson
            );
        }

        /// <summary>
        /// Switches perspective, carrying the current aim onto the incoming rig. Does nothing if that
        /// perspective is already selected. Pass <c>instant</c> to place the camera immediately instead
        /// of blending — for a scene load or a teleport, where a blend would sweep the camera across the
        /// level.
        /// </summary>
        public void SetPerspective(CameraPerspective perspective, bool instant = false) {
            if (perspective == Perspective) return;

            ICameraRig outgoingRig = ActiveRig;
            ICameraRig incomingRig = ResolveRig(perspective);
            CarryLookAngles(outgoingRig, incomingRig);

            Perspective = perspective;
            OnPerspectiveChanged?.Invoke(perspective);

            BeginBlend(instant);
        }

        /// <summary>
        /// Forwards a look delta in degrees to whichever rig is currently active. Deltas aimed at the
        /// idle rig would be thrown away at the next switch, so nothing is sent to it.
        /// </summary>
        public void AddLookInput(Vector2 degreesDelta) {
            ActiveRig?.AddLookInput(degreesDelta);
        }

        /// <summary>
        /// Points both rigs at a target, so the idle one is already framed correctly when it is switched
        /// to. Passing null parks both.
        /// </summary>
        public void SetTarget(Transform target) {
            if (firstPersonRig != null) {
                firstPersonRig.SetTarget(target);
            }

            if (thirdPersonRig != null) {
                thirdPersonRig.SetTarget(target);
            }
        }

        /// <remarks>
        /// Silently does nothing when either rig is missing: a rig left unassigned is a setup problem
        /// the missing view makes obvious, and refusing the switch outright would leave the player stuck
        /// in a perspective with no way to explain why.
        /// </remarks>
        private void CarryLookAngles(ICameraRig outgoingRig, ICameraRig incomingRig) {
            if (outgoingRig == null) return;
            if (incomingRig == null) return;

            incomingRig.SetLookAngles(outgoingRig.Yaw, outgoingRig.Pitch);
        }

        private void LateUpdate() {
            if (!_isBlending) {
                PinToActiveAnchor();
                return;
            }

            AdvanceBlend();
        }

        /// <remarks>
        /// The blend starts from the camera's live pose rather than the outgoing rig's anchor, so a
        /// switch that interrupts an unfinished blend continues from where the camera actually is
        /// instead of jumping back to the rig it was leaving.
        /// </remarks>
        private void BeginBlend(bool instant) {
            if (instant || blendDuration <= 0f || cameraTransform == null) {
                _isBlending = false;
                PinToActiveAnchor();
                return;
            }

            _blendStartPosition = cameraTransform.position;
            _blendStartRotation = cameraTransform.rotation;
            _blendElapsed = 0f;
            _isBlending = true;
        }

        /// <remarks>
        /// Interpolation is unclamped so an authored curve may overshoot past the incoming anchor and
        /// settle back, which is the usual way to give a perspective switch some snap. The default
        /// ease-in-out never leaves the zero-to-one range, so this costs nothing until someone asks for
        /// it.
        /// </remarks>
        private void AdvanceBlend() {
            Transform anchor = ActiveAnchor;

            if (cameraTransform == null || anchor == null) {
                _isBlending = false;
                return;
            }

            _blendElapsed += Time.deltaTime;
            float normalisedTime = Mathf.Clamp01(_blendElapsed / blendDuration);
            float weight = blendCurve.Evaluate(normalisedTime);

            cameraTransform.SetPositionAndRotation(
                Vector3.LerpUnclamped(_blendStartPosition, anchor.position, weight),
                Quaternion.SlerpUnclamped(_blendStartRotation, anchor.rotation, weight)
            );

            if (normalisedTime < 1f) return;

            _isBlending = false;
        }

        private void PinToActiveAnchor() {
            if (cameraTransform == null) return;

            Transform anchor = ActiveAnchor;
            if (anchor == null) return;

            cameraTransform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        /// <remarks>
        /// The concrete field is null-checked before it is widened to the interface: a destroyed or
        /// unassigned Unity object compares equal to null only through its own overloaded operator, and
        /// that operator is gone once the reference is held as an <see cref="ICameraRig"/>. Checking
        /// first means callers can rely on a plain null check against <see cref="ActiveRig"/>.
        /// </remarks>
        private ICameraRig ResolveRig(CameraPerspective perspective) {
            if (perspective == CameraPerspective.FirstPerson) {
                return firstPersonRig != null ? firstPersonRig : null;
            }

            return thirdPersonRig != null ? thirdPersonRig : null;
        }
    }
}
