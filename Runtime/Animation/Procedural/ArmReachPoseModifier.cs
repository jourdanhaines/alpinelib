using System;
using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Cameras;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Carries the animated hands round the eye as the actor looks up and down, so a prop held in front
    /// of the face keeps its distance and place in view.
    /// </summary>
    /// <remarks>
    /// <see cref="Capture"/> records the pure animated arms; <see cref="Apply"/> turns the hands' pose
    /// about the pitched eye (the look's own eye model) and solves each arm onto it with a two-bone IK
    /// (<see cref="ArmReachSolver"/>), writing absolute rotations so the torso share the look already
    /// gave the arms is replaced, not added to. Both hands share one shift along the view, so the
    /// spacing between them never changes:
    /// <list type="bullet">
    /// <item>with a <c>followStartPitch</c> the animated arms are taken as authored for the look at that
    /// pitch: the look's chest lean there (its share on the arms' ancestors) carries them first; the hands
    /// then turn by the look's difference from it (both ways, within <c>maxFollowPitchUp</c> above level
    /// and <c>maxFollowPitchDown</c>) about the eye at the start pitch, carried to the eye at the look, so
    /// a prop held on that line of sight stays on the view axis;</item>
    /// <item>with <c>cancelRootSway</c> (recommended for a view-rigid camera) the root bone's move away
    /// from <c>rootReference</c> is taken back out of the animated hands first, so a lower layer's hips
    /// (idle sway, walk bob, jump) do not swing them; they are also carried with the resting eye when a
    /// crouch lowers it. The shoulders still ride the hips unless <c>steadyTorso</c> turns the torso
    /// bone back by the same turn and leans it (≤ <c>steadyTorsoMaxDegrees</c>) to carry the shoulders
    /// back over the root's move; the pair shift below covers what is left (mostly the root's rise and fall);</item>
    /// <item>the elbow swivels, over the first <c>swivelBlendPitch</c> degrees followed, from the
    /// animated one to the least-roll one (the bend plane's normal along the palm, from
    /// <c>leftPalmLocal</c>/<c>rightPalmLocal</c>), or with no palm set to the one that bends the wrist
    /// least (the hand's long axis, +Y, reversed);</item>
    /// <item>the pair pushes out just enough that the carried prop's bottom (its centre's offset from the
    /// hands turned with them, less <c>carriedExtentDown</c>) clears the chest-front point (read after the
    /// torso is steadied; the clearance fades out over <c>clearanceFade</c> above <c>clearanceTop</c> of
    /// the prop bottom's height over that point), with a <c>pushDeadband</c> backlash, eased out at
    /// <c>pushOutSpeed</c> and back in at <c>viewShiftMaxSpeed</c>, settling faster from large gaps
    /// (<c>shiftSettleSeconds</c>);</item>
    /// <item>out of reach, the pair pulls in toward <c>minDistance</c>, the clavicles shrug toward the
    /// pulled-in targets, then both targets shift together by the least move that lets each arm reach
    /// (<see cref="PairShiftMetres"/>, rate-limited by <c>pairShiftMaxSpeed</c>), so the spacing and the
    /// hands' turn never change and a prop held between them stays rigid; each step starts where the last
    /// ends, so the hands move continuously. Where that cannot reach (or breaks the chest clearance) the
    /// followed pitch lags toward level at once and catches up at <c>followLagSpeed</c> with a
    /// <c>followLagHysteresis</c> (<see cref="FollowLagDegrees"/>); when no pitch reaches, the target
    /// is kept (<see cref="PairShiftClearanceViolated"/>). Eased state resets whenever the layer is not
    /// live (<see cref="ResetShifts"/>).</item>
    /// </list>
    /// Ordered at <see cref="DefaultOrder"/> (10), after the look at 0. The effective weight is
    /// <see cref="PoseModifier.Weight"/> times the optional follow layer's: the arms are solved at the
    /// full pitch and each written bone is blended from the look's pose toward the solve by it, so 0
    /// leaves the look's pose and 1 is the exact solve. Allocation free per frame.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class ArmReachPoseModifier : PoseModifier {
        /// <summary>Stack order new components get: after <see cref="LookPoseModifier"/> at 0.</summary>
        public const int DefaultOrder = 10;

        private const int ArmCount = 2;
        private const int LagSteps = 10;
        private const float ReachTolerance = 1e-4f;
        private const float ClearanceTolerance = 1e-4f;
        private const float LagTolerance = 1e-4f;

        [Tooltip("Look whose eye model the hands orbit; found on this object when left empty.")]
        [SerializeField] private LookPoseModifier look;

        [Tooltip("Optional layer blend whose weight scales this layer, e.g. the upper-body hold layer.")]
        [SerializeField] private AnimatorLayerBlend followLayer;

        [Tooltip("Resting eye in the actor's local space; match the first-person rig's eye offset.")]
        [SerializeField] private Vector3 eyeLocal = new Vector3(0f, 1.61f, 0f);

        [Tooltip("Camera target height the eye rests at; above zero, the eye drops with the target's height (crouching).")]
        [SerializeField] private float standingHeight;

        [Tooltip("Carried prop's centre relative to the hands' centre (hand bones), in the actor's frame at level look, in metres.")]
        [SerializeField] private Vector3 carriedCentreOffset;

        [Tooltip("How far the carried prop reaches below its centre, in metres.")]
        [SerializeField] private float carriedExtentDown = 0.133f;

        [Tooltip("Hold the hands against the body's own sway (recommended for a view-rigid camera): un-apply the root bone's move away from rootReference from the animated hands, so a lower layer's hips (idle sway, walk bob, jump) do not carry them. The shoulders still ride the hips; a hand they pull out of reach is met by shifting both targets together.")]
        [SerializeField] private bool cancelRootSway;

        [Tooltip("Root bone (Hips) pose in the actor's local space that the animated hands were authored over; set from the held clip's own first frame.")]
        [SerializeField] private Pose rootReference = Pose.identity;

        [Tooltip("With cancelRootSway, also turn the torso bone back by the root's turn away from rootReference, so the spine, chest and shoulders keep their reference orientation in the actor's frame and only the root's move still carries them.")]
        [SerializeField] private bool steadyTorso;

        [Tooltip("Bone steadyTorso counter-turns: the first spine bone under the root.")]
        [SerializeField] private HumanBodyBones torsoBone = HumanBodyBones.Spine;

        [Tooltip("Largest lean, in degrees, steadyTorso adds to carry the shoulders back over the root's move.")]
        [Range(0f, 45f)]
        [SerializeField] private float steadyTorsoMaxDegrees = 20f;

        [Tooltip("Gap kept between the carried prop's bottom and the chest-front point, in metres.")]
        [SerializeField] private float bodyClearance = 0.15f;

        [Tooltip("Front of the chest in UpperChest-local space (Chest when unmapped, else the actor's).")]
        [SerializeField] private Vector3 chestFrontLocal;

        [Tooltip("Nearest the hands' centre may pull in toward the eye, along the view, when out of reach.")]
        [SerializeField] private float minDistance = 0.24f;

        [Tooltip("Share of an arm's full length it may stretch to.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float reachLimit = 0.995f;

        [Tooltip("Largest clavicle turn, in degrees, toward a target out of reach.")]
        [Range(0f, 45f)]
        [SerializeField] private float clavicleMaxDegrees = 12f;

        [Tooltip("Look pitch (down) the hands start following from; below it, looking up included, the animated pose stays fixed in the body. 0 follows from level, both ways.")]
        [Range(0f, 85f)]
        [SerializeField] private float followStartPitch;

        [Tooltip("Degrees of followed pitch over which the elbow swivel moves from the animated one to the least-roll one (least wrist bend with no palm set).")]
        [Min(0f)]
        [SerializeField] private float swivelBlendPitch = 10f;

        [Tooltip("Direction the left palm faces, unit, in the left hand bone's local space; zero swivels for the least wrist bend instead of the least forearm roll.")]
        [SerializeField] private Vector3 leftPalmLocal;

        [Tooltip("Direction the right palm faces, unit, in the right hand bone's local space; zero swivels for the least wrist bend instead of the least forearm roll.")]
        [SerializeField] private Vector3 rightPalmLocal;

        [Tooltip("Fastest the shared shift of both hands (when a hand is out of reach) may grow or release, in metres per second; 0 snaps.")]
        [Min(0f)]
        [SerializeField] private float pairShiftMaxSpeed = 1.2f;

        [Tooltip("Fastest the shared shift may catch up when the eased view shift leaves a hand out of reach even with it, in metres per second; what it cannot cover stays a hand miss; 0 snaps.")]
        [Min(0f)]
        [SerializeField] private float pairShiftCatchUpSpeed = 3f;

        [Tooltip("Fastest the pair's shift along the view may come back in (the chest push-out releasing, a pull-in), in metres per second; 0 snaps.")]
        [Min(0f)]
        [SerializeField] private float viewShiftMaxSpeed = 0.25f;

        [Tooltip("Fastest the pair's shift along the view may move out (the chest push-out growing), in metres per second; 0 snaps.")]
        [Min(0f)]
        [SerializeField] private float pushOutSpeed = 0.5f;

        [Tooltip("Backlash on the chest push-out, in metres: the push only comes back in once the clearance asks for this much less, so breath-sized wobbles do not move the hands.")]
        [Min(0f)]
        [SerializeField] private float pushDeadband = 0.004f;

        [Tooltip("Floor on the cosine of the look pitch the chest clearance is divided by, so a steep look down does not magnify the chest's small moves into large pushes.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float clearanceMinCosine = 0.4f;

        [Tooltip("Height of the carried prop's bottom above the chest-front point, in metres, up to which the full body clearance is kept; see clearanceFade.")]
        [SerializeField] private float clearanceTop = 0.15f;

        [Tooltip("Height, in metres, over which the body clearance fades out above clearanceTop, so a prop held up in front of the face is not pushed off the chest; 0 keeps the full clearance at every height.")]
        [Min(0f)]
        [SerializeField] private float clearanceFade;

        [Tooltip("Time constant, in seconds, of the exponential settle the view shift also takes both ways, so a large gap (an open from a steep look) closes quickly while small ones keep the speed limits; 0 uses the speed limits alone.")]
        [Min(0f)]
        [SerializeField] private float shiftSettleSeconds = 0.15f;

        [Tooltip("Fastest the hands catch up with the look once they lag it, in degrees per second; 0 snaps.")]
        [Min(0f)]
        [SerializeField] private float followLagSpeed = 120f;

        [Tooltip("Degrees past the eased pitch that must also be reachable before the lag releases, so it does not flip at the reach edge.")]
        [Min(0f)]
        [SerializeField] private float followLagHysteresis = 1f;

        [Tooltip("With a follow start pitch: the highest look, in degrees above level, the hands follow up to (past it they stay in the body). Without one: the largest upward look pitch they follow.")]
        [Range(-85f, 85f)]
        [SerializeField] private float maxFollowPitchUp = 20f;

        [Tooltip("Largest downward look pitch the hands follow; looking further down they stay in the body.")]
        [Range(0f, 85f)]
        [SerializeField] private float maxFollowPitchDown = 85f;

        private readonly Transform[] _shoulders = new Transform[ArmCount];
        private readonly Transform[] _upperArms = new Transform[ArmCount];
        private readonly Transform[] _lowerArms = new Transform[ArmCount];
        private readonly Transform[] _hands = new Transform[ArmCount];
        private readonly Vector3[] _upperStart = new Vector3[ArmCount];
        private readonly Vector3[] _elbowStart = new Vector3[ArmCount];
        private readonly Vector3[] _handStart = new Vector3[ArmCount];
        private readonly Vector3[] _bendStart = new Vector3[ArmCount];
        private readonly bool[] _hasBend = new bool[ArmCount];
        private readonly Quaternion[] _upperRotationStart = new Quaternion[ArmCount];
        private readonly Quaternion[] _lowerRotationStart = new Quaternion[ArmCount];
        private readonly Quaternion[] _handRotationStart = new Quaternion[ArmCount];
        private readonly float[] _upperLength = new float[ArmCount];
        private readonly float[] _lowerLength = new float[ArmCount];
        private readonly Vector3[] _armPivot = new Vector3[ArmCount];
        private readonly Vector3[] _baseTarget = new Vector3[ArmCount];
        private readonly bool[] _shrugged = new bool[ArmCount];
        private readonly Quaternion[] _shoulderFrom = new Quaternion[ArmCount];
        private readonly Quaternion[] _upperFrom = new Quaternion[ArmCount];
        private readonly Quaternion[] _lowerFrom = new Quaternion[ArmCount];
        private readonly Quaternion[] _handFrom = new Quaternion[ArmCount];
        private readonly Quaternion[] _shoulderSolved = new Quaternion[ArmCount];
        private readonly Quaternion[] _upperSolved = new Quaternion[ArmCount];
        private readonly Quaternion[] _lowerSolved = new Quaternion[ArmCount];
        private readonly Quaternion[] _handSolved = new Quaternion[ArmCount];
        private readonly Vector3[] _solvedTarget = new Vector3[ArmCount];
        private readonly float[] _miss = new float[ArmCount];

        private Actor _actor;
        private Transform _actorRoot;
        private ICameraTarget _cameraTarget;
        private Transform _chestBone;
        private Func<float, bool> _isFeasible;
        private bool _captured;
        private Vector3 _restingEye;
        private Vector3 _pivotEye;
        private Vector3 _chestFront;
        private Quaternion _turn = Quaternion.identity;
        private Quaternion _lean = Quaternion.identity;
        private Transform _rootBone;
        private Transform _torso;
        private Quaternion _rootUndo = Quaternion.identity;
        private Vector3 _rootPosition;
        private bool _warnedNoJoints;
        private readonly List<LookPoseJoint> _jointScratch = new List<LookPoseJoint>();
        private readonly LookPoseJoint[][] _armJoints = new LookPoseJoint[ArmCount][];
        private Vector3 _view = Vector3.forward;
        private float _clearancePush;
        private float _heldPush;
        private float _viewShift;
        private bool _hasViewShift;
        private float _appliedPitch;
        private float _appliedPush;
        private Vector3 _pairShift;
        private Vector3 _lagPair;
        private float _lagShift;
        private float _compromiseShift;
        private bool _pairShiftClearanceViolated;
        private Vector3 _bottom;
        private float _lookPitch;
        private float _targetPitch;
        private float _followLag;
        private float _solvedPush;
        private float _followPitch;
        private bool _hasFollow;

        public ArmReachPoseModifier() : base(DefaultOrder) {
        }

        /// <summary>
        /// Followed pitch times the effective weight, in degrees (positive down), last apply: the look
        /// past <see cref="FollowStartPitch"/>; it falls short of that while lagging.
        /// </summary>
        public float AppliedPitch => IsLive ? _appliedPitch : 0f;

        /// <summary>Shift along the view times the effective weight, in metres, last apply: out to clear the chest, negative when pulled in.</summary>
        public float AppliedPush => IsLive ? _appliedPush : 0f;

        /// <summary>
        /// Distance, in metres, from <paramref name="arm"/>'s (0 left, 1 right) written hand bone to its
        /// target, last apply: 0 when the arm reached it at full weight; a partial weight blends it short.
        /// </summary>
        public float LastMissMetres(int arm) {
            if (!IsLive || arm < 0 || arm >= ArmCount) return 0f;

            return _miss[arm];
        }

        /// <summary>The larger of both arms' <see cref="LastMissMetres"/>.</summary>
        public float HandMissMetres => Mathf.Max(LastMissMetres(0), LastMissMetres(1));

        /// <summary>
        /// Distance, in metres, both hand targets moved together last apply because a hand was out of reach
        /// after the pull-in and shrug (the least shared move that lets each arm reach, rate-limited by
        /// <c>pairShiftMaxSpeed</c>); 0 when not shifting.
        /// </summary>
        public float PairShiftMetres => IsLive ? _pairShift.magnitude : 0f;

        /// <summary>
        /// True when last apply's pair shift broke the chest clearance at every pitch, so the hands kept the
        /// followed pitch and rode the shoulders closer to the chest.
        /// </summary>
        public bool PairShiftClearanceViolated => IsLive && _pairShiftClearanceViolated;

        /// <summary>Pitch, in degrees past the follow start (negative up), the hands would follow to last apply with no reach limit.</summary>
        public float TargetFollowPitch => IsLive ? _targetPitch : 0f;

        /// <summary>Degrees the followed pitch trails <see cref="TargetFollowPitch"/> last apply while out of reach; 0 when following.</summary>
        public float FollowLagDegrees => IsLive ? _followLag : 0f;

        /// <summary>Chest push-out, in metres, last apply after its backlash and before easing.</summary>
        public float SolvedPush => IsLive ? _solvedPush : 0f;

        /// <summary>See the <c>maxFollowPitchUp</c> field.</summary>
        public float MaxFollowPitchUp => maxFollowPitchUp;

        /// <summary>Largest downward look pitch the hands follow.</summary>
        public float MaxFollowPitchDown => maxFollowPitchDown;

        /// <summary>Look pitch the hands start following from; 0 follows from level, up and down.</summary>
        public float FollowStartPitch => followStartPitch;

        /// <summary>Effective weight this frame: the layer's own weight times the follow layer's.</summary>
        public float EffectiveWeight => followLayer != null ? Weight * followLayer.Weight : Weight;

        private bool IsLive => isActiveAndEnabled && Weight > 0f;

        /// <inheritdoc />
        public override void Bind(Transform actorRoot, PoseBones bones) {
            _actor = GetComponent<Actor>();
            _actorRoot = actorRoot;
            _cameraTarget = GetComponent<ICameraTarget>();
            _isFeasible = IsFeasible;
            ResetShifts();
            if (look == null) look = GetComponent<LookPoseModifier>();

            BindArm(bones, 0, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            BindArm(bones, 1, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);

            _rootBone = bones.Find(HumanBodyBones.Hips);
            _torso = bones.Find(torsoBone);
            _armJoints[0] = AncestorJoints(0);
            _armJoints[1] = AncestorJoints(1);
            _chestBone = bones.Find(HumanBodyBones.UpperChest);
            if (_chestBone == null) _chestBone = bones.Find(HumanBodyBones.Chest);
            _captured = false;
        }

        /// <inheritdoc />
        public override void Capture(PoseBones bones) {
            _captured = false;
            if (_actorRoot == null) return;

            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) CaptureArm(arm);
            }

            _captured = true;
        }

        /// <inheritdoc />
        public override void Apply(PoseBones bones, float deltaTime) {
            _appliedPitch = 0f;
            _appliedPush = 0f;
            _pairShiftClearanceViolated = false;
            ClearMiss();
            if (!_captured) {
                ResetShifts();
                return;
            }

            _captured = false;
            float weight = EffectiveWeight;
            if (weight <= 0f || (!IsMapped(0) && !IsMapped(1))) {
                ResetShifts();
                return;
            }

            _targetPitch = FollowedPitch(_actor.LookPitch);
            _restingEye = RestingEye();
            _pivotEye = StandingEyeAt(followStartPitch);
            CancelRootSway();
            LeanCapturedArms();
            SteadyTorso(bones, weight);
            _chestFront = ChestFront();
            ReadBlendSources();
            ReadArmPivots();

            float pitch = FollowPitch(bones, deltaTime);
            float shift = EaseShifts(SolveShift(pitch), deltaTime);
            float swivelShare = swivelBlendPitch > 0f ? Mathf.Clamp01(Mathf.Abs(pitch) / swivelBlendPitch) : 1f;
            Quaternion rootRotation = _actorRoot.rotation;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) SolveArm(arm, _baseTarget[arm] + _view * shift + _pairShift, rootRotation, swivelShare);
            }

            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) WriteArm(bones, arm, weight);
            }

            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) _miss[arm] = Vector3.Distance(_hands[arm].position, _actorRoot.TransformPoint(_solvedTarget[arm]));
            }

            _appliedPitch = pitch * weight;
            _appliedPush = shift * weight;
        }

        private void OnDisable() {
            _appliedPitch = 0f;
            _appliedPush = 0f;
            _captured = false;
            ResetShifts();
            _pairShiftClearanceViolated = false;
            ClearMiss();
        }

        /// <summary>
        /// Drops every eased state (view and pair shifts, push backlash, followed-pitch lag), so the next live
        /// frame starts from the solve, not from a pose last seen frames ago.
        /// </summary>
        public void ResetShifts() {
            _pairShift = Vector3.zero;
            _viewShift = 0f;
            _hasViewShift = false;
            _heldPush = 0f;
            _solvedPush = 0f;
            _followPitch = 0f;
            _followLag = 0f;
            _hasFollow = false;
        }

        /// <inheritdoc />
        protected internal override void Skip() {
            ResetShifts();
        }

        private void ClearMiss() {
            for (int arm = 0; arm < ArmCount; arm++) {
                _miss[arm] = 0f;
            }
        }

        private void BindArm(PoseBones bones, int arm, HumanBodyBones shoulder, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand) {
            _shoulders[arm] = bones.Find(shoulder);
            _upperArms[arm] = bones.Find(upper);
            _lowerArms[arm] = bones.Find(lower);
            _hands[arm] = bones.Find(hand);
        }

        private bool IsMapped(int arm) {
            return _upperArms[arm] != null && _lowerArms[arm] != null && _hands[arm] != null;
        }

        /// <summary>Records one arm of the animated pose in the actor's local space.</summary>
        private void CaptureArm(int arm) {
            Quaternion toLocal = Quaternion.Inverse(_actorRoot.rotation);
            Vector3 upper = _actorRoot.InverseTransformPoint(_upperArms[arm].position);
            Vector3 elbow = _actorRoot.InverseTransformPoint(_lowerArms[arm].position);
            Vector3 hand = _actorRoot.InverseTransformPoint(_hands[arm].position);

            _upperStart[arm] = upper;
            _elbowStart[arm] = elbow;
            _handStart[arm] = hand;
            _upperRotationStart[arm] = toLocal * _upperArms[arm].rotation;
            _lowerRotationStart[arm] = toLocal * _lowerArms[arm].rotation;
            _handRotationStart[arm] = toLocal * _hands[arm].rotation;
            _upperLength[arm] = Vector3.Distance(upper, elbow);
            _lowerLength[arm] = Vector3.Distance(elbow, hand);
            CaptureBend(arm, upper, elbow, hand);
        }

        /// <summary>
        /// Bend plane of the captured arm. A straight arm has none, so it keeps the last one seen, or
        /// takes one from the hand's turn before any was seen.
        /// </summary>
        private void CaptureBend(int arm, Vector3 upper, Vector3 elbow, Vector3 hand) {
            if (ArmReachSolver.TryBendNormal(upper, elbow, hand, out Vector3 bend)) {
                _bendStart[arm] = bend;
                _hasBend[arm] = true;
                return;
            }

            Vector3 reach = hand - upper;
            bool keepsLast = _hasBend[arm] && ArmReachSolver.PoleFromBend(reach.normalized, _bendStart[arm]).sqrMagnitude > 1e-4f;
            if (!keepsLast) _bendStart[arm] = ArmReachSolver.BendNormalFromHand(reach, _handRotationStart[arm]);
        }

        /// <summary>
        /// The pitch the hands follow this frame: the target pitch while every arm reaches it (after the
        /// pull-in, the shrug at the target and a pair shift that keeps the chest clearance); out of reach it
        /// lags toward level at once, as far as reaching needs, then eases back toward the target at
        /// <c>followLagSpeed</c> once a pitch <c>followLagHysteresis</c> further on reaches too. When no
        /// pitch from level reaches, the target is kept and the clearance given up.
        /// </summary>
        private float FollowPitch(PoseBones bones, float deltaTime) {
            float target = _targetPitch;
            TrySolveShift(target, false, out _);
            Shrug(bones, _compromiseShift);
            ReadArmPivots();

            float followed = _hasFollow ? NextFollowPitch(target, deltaTime) : LaggedPitch(target);
            _hasFollow = true;
            if (float.IsNaN(followed)) {
                // No pitch keeps the chest clear: keep the target, since a prop swung into the camera is
                // worse than one near the chest.
                _pairShiftClearanceViolated = true;
                followed = target;
            }

            _followPitch = followed;
            _followLag = target - followed;
            return followed;
        }

        /// <summary>
        /// Last frame's followed pitch carried on (the target itself when it was following): the lag grows at once when it no longer reaches, else
        /// releases toward <paramref name="target"/> at <c>followLagSpeed</c> while a pitch the hysteresis
        /// further on reaches too. NaN when no pitch from level reaches.
        /// </summary>
        private float NextFollowPitch(float target, float deltaTime) {
            bool following = Mathf.Abs(_followLag) <= LagTolerance;
            float held = following ? target : Mathf.Clamp(_followPitch, Mathf.Min(0f, target), Mathf.Max(0f, target));
            if (!IsFeasible(held)) return LaggedPitch(held);

            float step = followLagSpeed > 0f ? followLagSpeed * Mathf.Max(deltaTime, 0f) : float.PositiveInfinity;
            float eased = Mathf.MoveTowards(held, target, step);
            if (eased == held || IsFeasible(LagProbe(eased, target))) return eased;

            float reaching = held;
            float failing = eased;
            for (int index = 0; index < LagSteps; index++) {
                float middle = (reaching + failing) * 0.5f;
                if (IsFeasible(LagProbe(middle, target))) {
                    reaching = middle;
                } else {
                    failing = middle;
                }
            }

            return reaching;
        }

        private float LagProbe(float pitch, float target) {
            return Mathf.MoveTowards(pitch, target, followLagHysteresis);
        }

        /// <summary>The reaching pitch nearest <paramref name="wanted"/> on the way from level; NaN when level does not reach either.</summary>
        private float LaggedPitch(float wanted) {
            float lagged = ArmReachSolver.BisectToward(0f, wanted, LagSteps, _isFeasible);
            return lagged == wanted || IsFeasible(lagged) ? lagged : float.NaN;
        }

        /// <summary>
        /// True when every arm reaches its target at <paramref name="pitch"/>: by the view shift alone, or
        /// with a pair shift that brings both into reach and keeps the chest clearance. Fills
        /// <c>_lagShift</c>/<c>_lagPair</c>; measured against the raw chest push, not the held one.
        /// </summary>
        private bool IsFeasible(float pitch) {
            if (TrySolveShift(pitch, false, out float shift)) {
                _lagShift = shift;
                _lagPair = Vector3.zero;
                return true;
            }

            _lagShift = _compromiseShift;
            _lagPair = MinimalPairShift(_lagShift);
            return PairReaches(_lagShift, _lagPair) && PairKeepsClearance(_lagShift, _lagPair);
        }

        /// <summary>
        /// Lays out the followed <paramref name="pitch"/> for the solve and returns the shift along the view
        /// it wants, the chest push held by its backlash; out of reach, the compromise the pair shift
        /// covers from.
        /// </summary>
        private float SolveShift(float pitch) {
            return TrySolveShift(pitch, true, out float shift) ? shift : _compromiseShift;
        }

        /// <summary>
        /// Eases the applied view shift toward the <paramref name="solved"/> one, out at
        /// <c>pushOutSpeed</c> and back in at <c>viewShiftMaxSpeed</c> or faster by the exponential
        /// <c>shiftSettleSeconds</c> settle, and lets the pair shift, solved at
        /// that eased shift and eased at <c>pairShiftMaxSpeed</c>, cover the reach meanwhile. When the two
        /// cannot reach this frame the view shift moves toward the solved one only as far as reaching needs,
        /// and failing that the pair shift catches up at <c>pairShiftCatchUpSpeed</c>, what is left showing
        /// as a hand miss; both hands always move together. Snaps on the first live frame.
        /// </summary>
        private float EaseShifts(float solved, float deltaTime) {
            bool snaps = !_hasViewShift || deltaTime <= 0f;
            float outStep = snaps ? 0f : pushOutSpeed * deltaTime;
            float inStep = snaps ? 0f : viewShiftMaxSpeed * deltaTime;
            float pairStep = snaps ? 0f : pairShiftMaxSpeed * deltaTime;
            float catchUpStep = snaps ? 0f : pairShiftCatchUpSpeed * deltaTime;
            float settleShare = snaps ? 0f : ArmReachSolver.SettleShare(deltaTime, shiftSettleSeconds);
            float eased = ArmReachSolver.EaseShift(_viewShift, solved, outStep, inStep, settleShare);
            Vector3 pair = EasedPair(eased, pairStep);
            if (!PairReaches(eased, pair)) eased = ReachingViewShift(eased, solved, pairStep, catchUpStep, out pair);

            _viewShift = eased;
            _pairShift = pair;
            _hasViewShift = true;
            return eased;
        }

        /// <summary>The pair shift at view shift <paramref name="shift"/>, moved from the applied one by at most <paramref name="pairStep"/> (0 snaps).</summary>
        private Vector3 EasedPair(float shift, float pairStep) {
            Vector3 needed = MinimalPairShift(shift);
            return pairStep > 0f ? Vector3.MoveTowards(_pairShift, needed, pairStep) : needed;
        }

        /// <summary>
        /// The view shift nearest <paramref name="eased"/> toward <paramref name="solved"/> at which the
        /// eased pair shift lets both arms reach (bisection); when even the solved one does not, the eased
        /// view shift with the pair shift moved toward what it needs by <paramref name="catchUpStep"/>
        /// (0 snaps).
        /// </summary>
        private float ReachingViewShift(float eased, float solved, float pairStep, float catchUpStep, out Vector3 pair) {
            pair = EasedPair(solved, pairStep);
            if (!PairReaches(solved, pair)) {
                Vector3 needed = MinimalPairShift(eased);
                pair = catchUpStep > 0f ? Vector3.MoveTowards(_pairShift, needed, catchUpStep) : needed;
                return eased;
            }

            float reaching = solved;
            Vector3 reachingPair = pair;
            float shortShift = eased;
            for (int step = 0; step < LagSteps; step++) {
                float middle = (reaching + shortShift) * 0.5f;
                Vector3 middlePair = EasedPair(middle, pairStep);
                if (PairReaches(middle, middlePair)) {
                    reaching = middle;
                    reachingPair = middlePair;
                } else {
                    shortShift = middle;
                }
            }

            pair = reachingPair;
            return reaching;
        }

        /// <summary>True when every mapped arm reaches its target at view shift <paramref name="shift"/> plus <paramref name="pair"/>.</summary>
        private bool PairReaches(float shift, Vector3 pair) {
            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm)) continue;

                float reach = ArmReach(arm) + ReachTolerance;
                if ((_baseTarget[arm] + _view * shift + pair - _armPivot[arm]).sqrMagnitude > reach * reach) return false;
            }

            return true;
        }

        /// <summary>True when the carried prop's bottom, shifted with the pair, still keeps <c>bodyClearance</c> ahead of the chest.</summary>
        private bool PairKeepsClearance(float shift, Vector3 pair) {
            Vector3 bottom = _bottom + _view * shift + pair;
            return ClearanceShift(bottom, _lookPitch) <= ClearanceTolerance;
        }

        /// <summary>
        /// Least shift both targets (at view shift <paramref name="shift"/>) take together so every mapped
        /// arm reaches; see <see cref="ArmReachSolver.MinimalPairShift"/>.
        /// </summary>
        private Vector3 MinimalPairShift(float shift) {
            if (!IsMapped(0) || !IsMapped(1)) return SingleArmShift(IsMapped(0) ? 0 : 1, shift);

            return ArmReachSolver.MinimalPairShift(_baseTarget[0] + _view * shift, _armPivot[0], ArmReach(0), _baseTarget[1] + _view * shift, _armPivot[1], ArmReach(1));
        }

        private Vector3 SingleArmShift(int arm, float shift) {
            Vector3 target = _baseTarget[arm] + _view * shift;
            return ArmReachSolver.IntoReach(target, _armPivot[arm], ArmReach(arm));
        }

        private float ArmReach(int arm) => reachLimit * (_upperLength[arm] + _lowerLength[arm]);

        /// <summary>
        /// Carries each captured arm the way the look's chest lean at the follow start would (a rigid turn
        /// about the chain's pivots): the clip is authored for that lean, which this layer otherwise replaces.
        /// </summary>
        private void LeanCapturedArms() {
            _lean = Quaternion.identity;
            if (followStartPitch <= 0f || look == null) return;

            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) LeanCapturedArm(arm);
            }
        }

        private void LeanCapturedArm(int arm) {
            LookPoseJoint[] joints = ArmJoints(arm);
            if (joints.Length == 0) return;

            float lookWeight = look.Weight;
            _lean = Quaternion.AngleAxis(followStartPitch * look.AncestorShare(_upperArms[arm], true) * lookWeight, Vector3.right);
            Vector3 hand = _handStart[arm];
            Vector3 shift = hand + LookPoseSolver.ResolveTipOffset(joints, hand, followStartPitch, lookWeight) - _lean * hand;
            _upperStart[arm] = _lean * _upperStart[arm] + shift;
            _elbowStart[arm] = _lean * _elbowStart[arm] + shift;
            _handStart[arm] = _lean * hand + shift;
            _upperRotationStart[arm] = _lean * _upperRotationStart[arm];
            _lowerRotationStart[arm] = _lean * _lowerRotationStart[arm];
            _handRotationStart[arm] = _lean * _handRotationStart[arm];
            _bendStart[arm] = _lean * _bendStart[arm];
        }

        /// <summary>The look's joints carrying an arm, found once the look is bound.</summary>
        private LookPoseJoint[] ArmJoints(int arm) {
            return _armJoints[arm] ?? Array.Empty<LookPoseJoint>();
        }

        /// <summary>The look's joints carrying an arm, read at bind (the stack binds the look, order 0, first).</summary>
        private LookPoseJoint[] AncestorJoints(int arm) {
            if (look == null || _upperArms[arm] == null) return Array.Empty<LookPoseJoint>();

            _jointScratch.Clear();
            look.GetAncestorJoints(_upperArms[arm], _jointScratch);
            if (_jointScratch.Count == 0 && followStartPitch > 0f && !_warnedNoJoints) {
                _warnedNoJoints = true;
                Debug.LogWarning($"{nameof(ArmReachPoseModifier)} on '{name}': the look carries no joints above the arms at bind, so the follow start's chest lean is skipped; order the {nameof(LookPoseModifier)} before this layer and map its spine bones.", this);
            }

            return _jointScratch.ToArray();
        }

        /// <summary>
        /// Takes the root bone's move away from <c>rootReference</c> (actor space, rigid) back out of each
        /// captured arm, so the hands sit where the clip put
        /// them over its own root, not where a lower layer's hips carried them; the arms still solve from the
        /// live shoulders.
        /// </summary>
        private void CancelRootSway() {
            if (!cancelRootSway || _rootBone == null) return;

            Quaternion toLocal = Quaternion.Inverse(_actorRoot.rotation);
            Vector3 rootPosition = _actorRoot.InverseTransformPoint(_rootBone.position);
            Quaternion undo = rootReference.rotation * Quaternion.Inverse(toLocal * _rootBone.rotation);
            _rootUndo = undo;
            _rootPosition = rootPosition;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm)) continue;

                _upperStart[arm] = rootReference.position + undo * (_upperStart[arm] - rootPosition);
                _elbowStart[arm] = rootReference.position + undo * (_elbowStart[arm] - rootPosition);
                _handStart[arm] = rootReference.position + undo * (_handStart[arm] - rootPosition);
                _upperRotationStart[arm] = undo * _upperRotationStart[arm];
                _lowerRotationStart[arm] = undo * _lowerRotationStart[arm];
                _handRotationStart[arm] = undo * _handRotationStart[arm];
                _bendStart[arm] = undo * _bendStart[arm];
            }
        }

        /// <summary>
        /// Turns the torso bone back by the root's turn away from <c>rootReference</c> (the cancel's own
        /// undo), then leans it so the shoulders' midpoint returns to where the cancelled hands were solved
        /// against, both times <paramref name="weight"/>: the upper body holds still in the actor's frame
        /// while the hips sway, except for what a lean of at most <c>steadyTorsoMaxDegrees</c> about the
        /// torso pivot cannot carry (mostly the root's rise and fall).
        /// </summary>
        private void SteadyTorso(PoseBones bones, float weight) {
            if (!cancelRootSway || !steadyTorso || _rootBone == null || _torso == null) return;

            Quaternion worldUndo = _actorRoot.rotation * _rootUndo * Quaternion.Inverse(_actorRoot.rotation);
            worldUndo.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f) degrees -= 360f;
            if (Mathf.Abs(degrees) >= 1e-4f && float.IsFinite(axis.x)) bones.RotateWorld(_torso, axis, degrees * weight);

            SteadyShoulders(bones, weight);
        }

        /// <summary>
        /// Leans the torso bone so the shoulders' midpoint moves back by what the root's move still carries
        /// once its turn is undone: (root − reference) + (I − undo)·(torso − root), the same for every look.
        /// </summary>
        private void SteadyShoulders(PoseBones bones, float weight) {
            if (steadyTorsoMaxDegrees <= 0f || !TryShoulderMidpoint(out Vector3 midpoint)) return;

            Vector3 torso = _actorRoot.InverseTransformPoint(_torso.position);
            Vector3 lever = torso - _rootPosition;
            Vector3 carried = _rootPosition - rootReference.position + lever - _rootUndo * lever;
            Vector3 from = midpoint - torso;
            Vector3 to = from - carried;
            Vector3 axis = Vector3.Cross(from, to);
            if (axis.sqrMagnitude < 1e-12f) return;

            float degrees = Mathf.Min(Vector3.Angle(from, to), steadyTorsoMaxDegrees);
            bones.RotateWorld(_torso, _actorRoot.TransformDirection(axis.normalized), degrees * weight);
        }

        /// <summary>Midpoint of the mapped arms' upper-arm pivots as posed now, in the actor's local space.</summary>
        private bool TryShoulderMidpoint(out Vector3 midpoint) {
            midpoint = Vector3.zero;
            int count = 0;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm)) continue;

                midpoint += _actorRoot.InverseTransformPoint(_upperArms[arm].position);
                count++;
            }

            if (count == 0) return false;

            midpoint /= count;
            return true;
        }

        /// <summary>
        /// The pitch the hands turn by, before any lag: the look past the follow start, negative looking up
        /// from it, between <c>maxFollowPitchUp</c> above level and <c>maxFollowPitchDown</c>; with no
        /// follow start the look itself within both limits.
        /// </summary>
        private float FollowedPitch(float lookPitch) {
            if (followStartPitch <= 0f) return Mathf.Clamp(lookPitch, -Mathf.Max(0f, maxFollowPitchUp), maxFollowPitchDown);

            return Mathf.Clamp(lookPitch - followStartPitch, -(followStartPitch + maxFollowPitchUp), Mathf.Max(0f, maxFollowPitchDown - followStartPitch));
        }

        /// <summary>
        /// Lays out the hands' targets at followed <paramref name="pitch"/> (the hands turned by it about
        /// the eye at the follow start, carried to the eye at the look) and finds the shift along the view
        /// nearest the chest clearance that keeps both in reach. False, with the clearance shift, when
        /// none does.
        /// </summary>
        private bool TrySolveShift(float pitch, bool holdsPush, out float shift) {
            float lookPitch = followStartPitch + pitch;
            _turn = Quaternion.AngleAxis(pitch, Vector3.right);
            _view = Quaternion.AngleAxis(lookPitch, Vector3.right) * Vector3.forward;
            Vector3 eye = EyeAt(lookPitch);
            Vector3 centre = LayOutTargets(eye);
            Vector3 bottom = centre + _turn * (_lean * (carriedCentreOffset + new Vector3(0f, -carriedExtentDown, 0f)));
            _bottom = bottom;
            _lookPitch = lookPitch;

            float clearanceFloor = ClearanceShift(bottom, lookPitch);
            _clearancePush = Mathf.Max(0f, clearanceFloor);
            float push = _clearancePush;
            if (holdsPush) {
                _heldPush = ArmReachSolver.Backlash(_heldPush, _clearancePush, pushDeadband);
                _solvedPush = _heldPush;
                push = _heldPush;
            }

            shift = push;
            float pullFloor = Mathf.Min(push, minDistance - Vector3.Dot(centre - eye, _view));
            float lowest = Mathf.Max(clearanceFloor, pullFloor);
            float floor = lowest;
            float armLowest = float.NegativeInfinity;
            float armHighest = float.PositiveInfinity;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm)) continue;

                if (!ArmReachSolver.ReachInterval(_baseTarget[arm], _armPivot[arm], _view, ArmReach(arm), out float min, out float max)) {
                    // Out of reach along the whole view line: its nearest approach stands in for both ends.
                    min = Vector3.Dot(_armPivot[arm] - _baseTarget[arm], _view);
                    max = min;
                }

                armLowest = Mathf.Max(armLowest, min);
                armHighest = Mathf.Min(armHighest, max);
            }

            lowest = Mathf.Max(lowest, armLowest);
            float highest = Mathf.Min(push, armHighest);
            if (lowest <= highest) {
                shift = highest;
                _compromiseShift = highest;
                return true;
            }

            // Out of reach: the view shift the shrug and pair shift start from, matching the solve at the edge.
            _compromiseShift = Mathf.Clamp((armLowest + armHighest) * 0.5f, floor, push);
            return false;
        }

        /// <summary>Shift along the view that brings <paramref name="bottom"/> to the body clearance (faded by its height over the chest) at <paramref name="lookPitch"/>.</summary>
        private float ClearanceShift(Vector3 bottom, float lookPitch) {
            float clearance = ArmReachSolver.FadedClearance(_chestFront, bottom, Vector3.up, bodyClearance, clearanceTop, clearanceFade);
            return ArmReachSolver.ClearanceShift(_chestFront, bottom, Vector3.forward, clearance, lookPitch, clearanceMinCosine);
        }

        /// <summary>Fills each hand's unshifted target: turned about the follow-start eye, placed at <paramref name="eye"/>; returns their centre.</summary>
        private Vector3 LayOutTargets(Vector3 eye) {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm)) continue;

                _baseTarget[arm] = eye + _turn * (_handStart[arm] - _pivotEye);
                sum += _baseTarget[arm];
                count++;
            }

            return count > 0 ? sum / count : eye;
        }

        /// <summary>Turns each clavicle, within its limit, toward its hand's target at view shift <paramref name="shift"/>.</summary>
        private void Shrug(PoseBones bones, float shift) {
            if (clavicleMaxDegrees <= 0f) return;

            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm) || _shoulders[arm] == null) continue;

                ShrugArm(bones, arm, shift);
            }
        }

        private void ShrugArm(PoseBones bones, int arm, float shift) {
            Vector3 clavicle = _actorRoot.InverseTransformPoint(_shoulders[arm].position);
            Vector3 target = _baseTarget[arm] + _view * shift;
            float reach = reachLimit * (_upperLength[arm] + _lowerLength[arm]);
            float degrees = Mathf.Min(ArmReachSolver.ShrugDegrees(clavicle, _armPivot[arm], target, reach), clavicleMaxDegrees);
            Vector3 axis = Vector3.Cross(_armPivot[arm] - clavicle, target - clavicle);
            if (degrees <= 0f || axis.sqrMagnitude < 1e-12f) return;

            bones.RotateWorld(_shoulders[arm], _actorRoot.TransformDirection(axis.normalized), degrees);
            _shoulderSolved[arm] = _shoulders[arm].rotation;
            _shrugged[arm] = true;
        }

        /// <summary>
        /// Two-bone solve of one arm onto <paramref name="target"/>, twist carried from the captured
        /// pose, the elbow swivel moved by <paramref name="swivelShare"/> from the captured one to the
        /// least forearm roll (least wrist bend with no palm set); fills the solved world rotations
        /// without writing them.
        /// </summary>
        private void SolveArm(int arm, Vector3 target, Quaternion rootRotation, float swivelShare) {
            Vector3 upperStart = _upperStart[arm];
            Vector3 elbowStart = _elbowStart[arm];
            Vector3 handStart = _handStart[arm];
            Vector3 bendStart = _bendStart[arm];
            Vector3 pivot = _armPivot[arm];
            Vector3 capturedPole = _turn * ArmReachSolver.PoleFromBend((handStart - upperStart).normalized, bendStart);
            Vector3 handAxis = _turn * (_handRotationStart[arm] * Vector3.up);
            Vector3 palm = _turn * (_handRotationStart[arm] * PalmLocal(arm));
            Vector3 pole = ArmReachSolver.SwivelPole(pivot, target, _upperLength[arm], _lowerLength[arm], handAxis, palm, capturedPole, swivelShare);
            _solvedTarget[arm] = target;

            Vector3 elbow = ArmReachSolver.SolveElbow(pivot, target, _upperLength[arm], _lowerLength[arm], pole);
            Vector3 bend = ArmReachSolver.BendNormal(pivot, elbow, target, _turn * bendStart);
            Quaternion upper = ArmReachSolver.Reorient(elbowStart - upperStart, bendStart, elbow - pivot, bend, _upperRotationStart[arm]);

            // Where the elbow lands once the upper arm takes its solved rotation, from the bone as it is now.
            Quaternion toLocal = Quaternion.Inverse(_actorRoot.rotation);
            Quaternion upperTurn = upper * Quaternion.Inverse(toLocal * _upperArms[arm].rotation);
            Vector3 elbowNow = pivot + upperTurn * (_actorRoot.InverseTransformPoint(_lowerArms[arm].position) - pivot);
            Quaternion lower = ArmReachSolver.Reorient(handStart - elbowStart, bendStart, target - elbowNow, bend, _lowerRotationStart[arm]);

            _upperSolved[arm] = rootRotation * upper;
            _lowerSolved[arm] = rootRotation * lower;
            _handSolved[arm] = rootRotation * (_turn * _handRotationStart[arm]);
        }

        private Vector3 PalmLocal(int arm) {
            return arm == 0 ? leftPalmLocal : rightPalmLocal;
        }

        /// <summary>Writes one arm top-down, each bone blended from the look's pose toward its solve.</summary>
        private void WriteArm(PoseBones bones, int arm, float weight) {
            if (_shrugged[arm]) bones.RotateWorldTo(_shoulders[arm], Blend(_shoulderFrom[arm], _shoulderSolved[arm], weight));

            bones.RotateWorldTo(_upperArms[arm], Blend(_upperFrom[arm], _upperSolved[arm], weight));
            bones.RotateWorldTo(_lowerArms[arm], Blend(_lowerFrom[arm], _lowerSolved[arm], weight));
            bones.RotateWorldTo(_hands[arm], Blend(_handFrom[arm], _handSolved[arm], weight));
        }

        private static Quaternion Blend(Quaternion from, Quaternion to, float weight) {
            return weight >= 1f ? to : Quaternion.Slerp(from, to, weight);
        }

        /// <summary>World rotations the look left the arms with, before any shrug: the pose weight 0 keeps.</summary>
        private void ReadBlendSources() {
            for (int arm = 0; arm < ArmCount; arm++) {
                _shrugged[arm] = false;
                if (!IsMapped(arm)) continue;

                if (_shoulders[arm] != null) _shoulderFrom[arm] = _shoulders[arm].rotation;
                _upperFrom[arm] = _upperArms[arm].rotation;
                _lowerFrom[arm] = _lowerArms[arm].rotation;
                _handFrom[arm] = _hands[arm].rotation;
            }
        }

        /// <summary>Current upper-arm pivots, after the look and any shrug, in the actor's local space.</summary>
        private void ReadArmPivots() {
            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) _armPivot[arm] = _actorRoot.InverseTransformPoint(_upperArms[arm].position);
            }
        }

        /// <summary>
        /// Resting eye, lowered with a crouching camera target when a standing height is set; a target
        /// reporting no height (its capsule not yet found) leaves the eye standing.
        /// </summary>
        private Vector3 RestingEye() {
            if (_cameraTarget == null || standingHeight <= 0f || _cameraTarget.Height <= 0f) return eyeLocal;

            Vector3 eye = eyeLocal;
            eye.y = Mathf.Max(eyeLocal.y + _cameraTarget.Height - standingHeight, 0f);
            return eye;
        }

        private Vector3 EyeAt(float pitch) {
            if (look == null) return _restingEye;

            return _restingEye + look.ResolveEyeOffset(pitch, _restingEye);
        }

        /// <summary>
        /// The standing eye at <paramref name="pitch"/>: the hands are authored around it, so measured from
        /// it a crouch (the resting eye dropped) carries them down with the eye.
        /// </summary>
        private Vector3 StandingEyeAt(float pitch) {
            if (look == null) return eyeLocal;

            return eyeLocal + look.ResolveEyeOffset(pitch, eyeLocal);
        }

        /// <summary>The chest-front point as the torso is bent this frame, in the actor's local space.</summary>
        private Vector3 ChestFront() {
            if (_chestBone == null) return chestFrontLocal;

            return _actorRoot.InverseTransformPoint(_chestBone.TransformPoint(chestFrontLocal));
        }
    }
}
