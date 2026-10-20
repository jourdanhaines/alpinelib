using System;
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
    /// <item>looking down, the pair pushes out just enough that its bottom clears the chest-front point;</item>
    /// <item>out of reach, the clavicles shrug a little, then the pair pulls in toward
    /// <c>minDistance</c>, then the hands stop following and lag behind the look.</item>
    /// </list>
    /// Ordered at <see cref="DefaultOrder"/> (10), after the look at 0. The effective weight is
    /// <see cref="PoseModifier.Weight"/> times the optional follow layer's: the arms are solved at the
    /// full pitch and each written bone is blended from the look's pose toward the solve by it, so 0
    /// leaves the look's pose and 1 is the exact solve. Allocation free and time free per frame.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class ArmReachPoseModifier : PoseModifier {
        /// <summary>Stack order new components get: after <see cref="LookPoseModifier"/> at 0.</summary>
        public const int DefaultOrder = 10;

        private const int ArmCount = 2;
        private const int LagSteps = 10;

        [Tooltip("Look whose eye model the hands orbit; found on this object when left empty.")]
        [SerializeField] private LookPoseModifier look;

        [Tooltip("Optional layer blend whose weight scales this layer, e.g. the upper-body hold layer.")]
        [SerializeField] private AnimatorLayerBlend followLayer;

        [Tooltip("Resting eye in the actor's local space; match the first-person rig's eye offset.")]
        [SerializeField] private Vector3 eyeLocal = new Vector3(0f, 1.61f, 0f);

        [Tooltip("Camera target height the eye rests at; above zero, the eye drops with the target's height (crouching).")]
        [SerializeField] private float standingHeight;

        [Tooltip("How far the carried prop reaches below the hands' centre, in metres.")]
        [SerializeField] private float carriedExtentDown = 0.133f;

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

        [Tooltip("Largest upward look pitch the hands follow.")]
        [Range(0f, 85f)]
        [SerializeField] private float maxFollowPitchUp = 20f;

        [Tooltip("Largest downward look pitch the hands follow.")]
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

        private Actor _actor;
        private Transform _actorRoot;
        private ICameraTarget _cameraTarget;
        private Transform _chestBone;
        private Func<float, bool> _isReachableAt;
        private bool _captured;
        private Vector3 _restingEye;
        private Vector3 _chestFront;
        private Quaternion _turn = Quaternion.identity;
        private Vector3 _view = Vector3.forward;
        private float _clearancePush;
        private float _appliedPitch;
        private float _appliedPush;

        public ArmReachPoseModifier() : base(DefaultOrder) {
        }

        /// <summary>
        /// Followed pitch times the effective weight, in degrees (positive down), last apply; the
        /// followed pitch falls short of the look while lagging.
        /// </summary>
        public float AppliedPitch => IsLive ? _appliedPitch : 0f;

        /// <summary>Shift along the view times the effective weight, in metres, last apply: out to clear the chest, negative when pulled in.</summary>
        public float AppliedPush => IsLive ? _appliedPush : 0f;

        /// <summary>Effective weight this frame: the layer's own weight times the follow layer's.</summary>
        public float EffectiveWeight => followLayer != null ? Weight * followLayer.Weight : Weight;

        private bool IsLive => isActiveAndEnabled && Weight > 0f;

        /// <inheritdoc />
        public override void Bind(Transform actorRoot, PoseBones bones) {
            _actor = GetComponent<Actor>();
            _actorRoot = actorRoot;
            _cameraTarget = GetComponent<ICameraTarget>();
            _isReachableAt = IsReachableAt;
            if (look == null) look = GetComponent<LookPoseModifier>();

            BindArm(bones, 0, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            BindArm(bones, 1, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);

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
            if (!_captured) return;

            _captured = false;
            float weight = EffectiveWeight;
            if (weight <= 0f || (!IsMapped(0) && !IsMapped(1))) return;

            float pitch = Mathf.Clamp(_actor.LookPitch, -maxFollowPitchUp, maxFollowPitchDown);
            _restingEye = RestingEye();
            _chestFront = ChestFront();
            ReadBlendSources();
            ReadArmPivots();

            float shift = ResolveShift(bones, ref pitch);
            Quaternion rootRotation = _actorRoot.rotation;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) SolveArm(arm, _baseTarget[arm] + _view * shift, rootRotation);
            }

            for (int arm = 0; arm < ArmCount; arm++) {
                if (IsMapped(arm)) WriteArm(bones, arm, weight);
            }

            _appliedPitch = pitch * weight;
            _appliedPush = shift * weight;
        }

        private void OnDisable() {
            _appliedPitch = 0f;
            _appliedPush = 0f;
            _captured = false;
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
        /// The shift along the view both hands take, after any clavicle shrug; lowers
        /// <paramref name="pitch"/> toward level when no shift keeps both hands in reach.
        /// </summary>
        private float ResolveShift(PoseBones bones, ref float pitch) {
            if (TrySolveShift(pitch, out float shift)) return shift;

            Shrug(bones);
            ReadArmPivots();
            if (TrySolveShift(pitch, out shift)) return shift;

            pitch = ArmReachSolver.BisectToward(0f, pitch, LagSteps, _isReachableAt);
            TrySolveShift(pitch, out shift);
            return shift;
        }

        private bool IsReachableAt(float pitch) => TrySolveShift(pitch, out _);

        /// <summary>
        /// Lays out the hands' targets at <paramref name="pitch"/> and finds the shift along the view
        /// nearest the chest clearance that keeps both in reach. False, with the clearance shift, when
        /// none does.
        /// </summary>
        private bool TrySolveShift(float pitch, out float shift) {
            _turn = Quaternion.AngleAxis(pitch, Vector3.right);
            _view = _turn * Vector3.forward;
            Vector3 eye = EyeAt(pitch);
            Vector3 centre = LayOutTargets(eye);
            Vector3 bottom = centre + _turn * new Vector3(0f, -carriedExtentDown, 0f);

            float clearanceFloor = ArmReachSolver.ClearanceShift(_chestFront, bottom, Vector3.forward, bodyClearance, pitch);
            _clearancePush = Mathf.Max(0f, clearanceFloor);
            shift = _clearancePush;

            float pullFloor = Mathf.Min(_clearancePush, minDistance - Vector3.Dot(centre - eye, _view));
            float lowest = Mathf.Max(clearanceFloor, pullFloor);
            float highest = _clearancePush;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm)) continue;

                float reach = reachLimit * (_upperLength[arm] + _lowerLength[arm]);
                if (!ArmReachSolver.ReachInterval(_baseTarget[arm], _armPivot[arm], _view, reach, out float min, out float max)) return false;

                lowest = Mathf.Max(lowest, min);
                highest = Mathf.Min(highest, max);
            }

            if (lowest > highest) return false;

            shift = highest;
            return true;
        }

        /// <summary>Fills each hand's unshifted target about <paramref name="eye"/>; returns their centre.</summary>
        private Vector3 LayOutTargets(Vector3 eye) {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm)) continue;

                _baseTarget[arm] = eye + _turn * (_handStart[arm] - _restingEye);
                sum += _baseTarget[arm];
                count++;
            }

            return count > 0 ? sum / count : eye;
        }

        /// <summary>Turns each clavicle, within its limit, toward its hand's target at the clearance shift.</summary>
        private void Shrug(PoseBones bones) {
            if (clavicleMaxDegrees <= 0f) return;

            for (int arm = 0; arm < ArmCount; arm++) {
                if (!IsMapped(arm) || _shoulders[arm] == null) continue;

                ShrugArm(bones, arm);
            }
        }

        private void ShrugArm(PoseBones bones, int arm) {
            Vector3 clavicle = _actorRoot.InverseTransformPoint(_shoulders[arm].position);
            Vector3 target = _baseTarget[arm] + _view * _clearancePush;
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
        /// pose; fills the solved world rotations without writing them.
        /// </summary>
        private void SolveArm(int arm, Vector3 target, Quaternion rootRotation) {
            Vector3 upperStart = _upperStart[arm];
            Vector3 elbowStart = _elbowStart[arm];
            Vector3 handStart = _handStart[arm];
            Vector3 bendStart = _bendStart[arm];
            Vector3 pole = _turn * ArmReachSolver.PoleFromBend((handStart - upperStart).normalized, bendStart);

            Vector3 pivot = _armPivot[arm];
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

        /// <summary>Resting eye, lowered with a crouching camera target when a standing height is set.</summary>
        private Vector3 RestingEye() {
            if (_cameraTarget == null || standingHeight <= 0f) return eyeLocal;

            Vector3 eye = eyeLocal;
            eye.y = Mathf.Max(eyeLocal.y + _cameraTarget.Height - standingHeight, 0f);
            return eye;
        }

        private Vector3 EyeAt(float pitch) {
            if (look == null) return _restingEye;

            return _restingEye + look.ResolveEyeOffset(pitch, _restingEye);
        }

        /// <summary>The chest-front point as the torso is bent this frame, in the actor's local space.</summary>
        private Vector3 ChestFront() {
            if (_chestBone == null) return chestFrontLocal;

            return _actorRoot.InverseTransformPoint(_chestBone.TransformPoint(chestFrontLocal));
        }
    }
}
