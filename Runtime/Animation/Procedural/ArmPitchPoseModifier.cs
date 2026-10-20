using System.Collections.Generic;
using AlpineLib.Actors;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Swings both upper arms with the look so a point carried in front of the chest — a held map, a
    /// book — stays on the view axis as the actor looks up and down.
    /// </summary>
    /// <remarks>
    /// The torso bones a <see cref="LookPoseModifier"/> bends already carry the arms part of the way;
    /// this layer adds the rest. The swing per pitch is solved once into a table by
    /// <see cref="ArmPitchSolver"/> against the resting skeleton and the look's own eye model, so the
    /// carried point is seen <c>viewOffsetDegrees</c> off the view axis at every pitch (negative holds
    /// it below). Without a look modifier the eye stays put and the torso takes nothing.
    ///
    /// Ordered at <see cref="DefaultOrder"/> (10), after the look at 0. Both turn bones about the
    /// actor's right axis, and turns about parallel axes commute, so the order is for readability, not
    /// correctness. The table is built on the first apply, after every layer has bound, and rebuilt
    /// when the look's weight or enabled state changes; call <see cref="Rebuild"/> after editing fields.
    /// The effective weight is <see cref="PoseModifier.Weight"/> times the optional follow layer's
    /// weight, so the arms follow only while a hold animation is blended in.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class ArmPitchPoseModifier : PoseModifier {
        /// <summary>Stack order new components get: after <see cref="LookPoseModifier"/> at 0.</summary>
        public const int DefaultOrder = 10;
        [Tooltip("Look whose torso share and eye model the arms follow; found on this object when left empty.")]
        [SerializeField] private LookPoseModifier look;

        [Tooltip("Optional layer blend whose weight scales this layer, e.g. the upper-body hold layer.")]
        [SerializeField] private AnimatorLayerBlend followLayer;

        [Tooltip("Point the arms carry, in the actor's local space and the resting pose.")]
        [SerializeField] private Vector3 carriedPointLocal = new Vector3(0f, 1.61f, 0.29f);

        [Tooltip("Resting eye in the actor's local space; match the first-person rig's eye offset.")]
        [SerializeField] private Vector3 eyeLocal = new Vector3(0f, 1.61f, 0f);

        [Tooltip("Degrees the carried point is held off the view axis; negative holds it below.")]
        [SerializeField] private float viewOffsetDegrees = -5f;

        [Tooltip("Largest look pitch, either way, the arms follow.")]
        [Range(0f, 85f)]
        [SerializeField] private float maxFollowPitch = 85f;

        private readonly float[] _table = new float[ArmPitchSolver.TableLength];
        private readonly List<LookPoseJoint> _torsoJoints = new List<LookPoseJoint>();
        private Actor _actor;
        private Transform _actorRoot;
        private Transform _leftUpperArm;
        private Transform _rightUpperArm;
        private Vector3 _restingShoulder;
        private bool _tableBuilt;
        private float _builtLookWeight;

        public ArmPitchPoseModifier() : base(DefaultOrder) {
        }

        /// <summary>Effective weight this frame: the layer's own weight times the follow layer's.</summary>
        public float EffectiveWeight => followLayer != null ? Weight * followLayer.Weight : Weight;

        /// <inheritdoc />
        public override void Bind(Transform actorRoot, PoseBones bones) {
            _actor = GetComponent<Actor>();
            _actorRoot = actorRoot;
            if (look == null) look = GetComponent<LookPoseModifier>();

            _leftUpperArm = bones.Find(HumanBodyBones.LeftUpperArm);
            _rightUpperArm = bones.Find(HumanBodyBones.RightUpperArm);
            _restingShoulder = RestingShoulder();
            _tableBuilt = false;
        }

        /// <inheritdoc />
        public override void Apply(PoseBones bones, float deltaTime) {
            if (_leftUpperArm == null && _rightUpperArm == null) return;

            float weight = EffectiveWeight;
            if (weight <= 0f) return;

            if (!_tableBuilt || EffectiveLookWeight() != _builtLookWeight) Rebuild();

            float pitch = Mathf.Clamp(_actor.LookPitch, -maxFollowPitch, maxFollowPitch);
            float swing = ArmPitchSolver.Sample(_table, pitch) * weight;
            Vector3 pitchAxis = _actorRoot.right;
            bones.RotateWorld(_leftUpperArm, pitchAxis, swing);
            bones.RotateWorld(_rightUpperArm, pitchAxis, swing);
        }

        /// <summary>Arm swing in degrees at <paramref name="pitch"/>, before weighting; builds the table if needed.</summary>
        public float SampleSwing(float pitch) {
            if (!_tableBuilt) Rebuild();

            return ArmPitchSolver.Sample(_table, pitch);
        }

        /// <summary>Re-solves the swing table against the current look and fields.</summary>
        public void Rebuild() {
            _tableBuilt = true;
            Transform arm = _leftUpperArm != null ? _leftUpperArm : _rightUpperArm;

            _torsoJoints.Clear();
            if (look != null) look.GetAncestorJoints(arm, _torsoJoints);

            float shareDown = look != null ? look.AncestorShare(arm, true) : 0f;
            float shareUp = look != null ? look.AncestorShare(arm, false) : 0f;
            float lookWeight = EffectiveLookWeight();
            _builtLookWeight = lookWeight;
            ArmPitchSolver.BuildTable(_table, _torsoJoints.ToArray(), lookWeight, _restingShoulder, carriedPointLocal,
                EyeAtPitch, viewOffsetDegrees, shareDown, shareUp);
        }

        /// <summary>
        /// Records <paramref name="worldPoint"/> as the carried point, relative to the shoulders as they
        /// are now and re-expressed against the resting shoulders. Call in the hold pose at level look,
        /// before this layer has swung the arms (or with its weight at zero).
        /// </summary>
        public bool CalibrateCarriedPoint(Vector3 worldPoint) {
            if (_actorRoot == null || (_leftUpperArm == null && _rightUpperArm == null)) return false;

            Vector3 currentShoulder = RestingShoulder();
            carriedPointLocal = _restingShoulder + (_actorRoot.InverseTransformPoint(worldPoint) - currentShoulder);
            _tableBuilt = false;
            return true;
        }

        /// <summary>Weight the look bends with: zero while it is missing or disabled.</summary>
        private float EffectiveLookWeight() {
            return look != null && look.isActiveAndEnabled ? look.Weight : 0f;
        }

        private Vector3 EyeAtPitch(float pitch) {
            if (look == null) return eyeLocal;

            return eyeLocal + look.ResolveEyeOffset(pitch, eyeLocal);
        }

        /// <summary>Midpoint of the upper-arm pivots in the actor's local space, in the current pose.</summary>
        private Vector3 RestingShoulder() {
            if (_leftUpperArm == null && _rightUpperArm == null) return Vector3.zero;
            if (_leftUpperArm == null) return _actorRoot.InverseTransformPoint(_rightUpperArm.position);
            if (_rightUpperArm == null) return _actorRoot.InverseTransformPoint(_leftUpperArm.position);

            return _actorRoot.InverseTransformPoint((_leftUpperArm.position + _rightUpperArm.position) * 0.5f);
        }
    }
}
