using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// Re-centres the casting pose while an upper-body skill is active: a corrective yaw is spread
    /// across the spine chain to restore the torso twist the avatar mask discards, and the humanoid
    /// look-at IK keeps the head on the aim point.
    /// </summary>
    /// <remarks>
    /// Upper-body skill clips play through an avatar mask that hands the hips to the locomotion
    /// layer. A clip authored with the hips twisted — which is how most casts and shots square the
    /// working arm to the target — loses that twist, and the arm lands off to one side of where the
    /// actor faces. The fix is mechanical: while the skill runs, the discarded twist is re-applied as
    /// a yaw distributed evenly over the Spine, Chest and UpperChest bones after the animator has
    /// posed them. <see cref="correctiveTwistDegrees"/> is the knob to tune in play mode until the
    /// arm sits centre; positive values turn the torso clockwise seen from above, and a clip twisted
    /// the other way simply takes a negative value.
    ///
    /// The spine twist runs in <c>LateUpdate</c> and needs no IK pass. The head look-at still goes
    /// through <c>OnAnimatorIK</c>, so it only contributes when the animator's base layer has its IK
    /// pass enabled — without it the twist still works and only the head garnish is lost.
    ///
    /// The component must live on the same GameObject as the <see cref="Animator"/> —
    /// <c>OnAnimatorIK</c> only fires there, and the bone pose must be modified after that animator
    /// writes it. <see cref="SkillSystem"/> adds one to its actor's animator automatically when none
    /// is present, so hand-placing it on a prefab is only needed to override the defaults.
    /// </remarks>
    [DisallowMultipleComponent]
    public class UpperBodyAim : MonoBehaviour {
        [Tooltip("Yaw in degrees re-applied across the spine while an upper-body skill runs. Tune until the casting arm points where the actor faces; negative twists the other way.")]
        [SerializeField] private float correctiveTwistDegrees = 35f;

        [Tooltip("How strongly the head turns toward the aim point. Needs the animator's IK pass.")]
        [Range(0f, 1f)]
        [SerializeField] private float headWeight = 0.3f;

        [Tooltip("How much of the head motion is clamped; higher values keep the pose closer to the clip.")]
        [Range(0f, 1f)]
        [SerializeField] private float clampWeight = 0.5f;

        [Tooltip("Metres ahead of the actor the aim point sits.")]
        [SerializeField] private float aimDistance = 10f;

        [Tooltip("Height of the aim point above the actor's feet, roughly chest to eye level.")]
        [SerializeField] private float aimHeight = 1.5f;

        [Tooltip("Blend-in and blend-out speed, in weight units per second.")]
        [SerializeField] private float blendSpeed = 8f;

        private readonly List<Transform> _spineBones = new();

        private Animator _animator;
        private SkillSystem _skills;
        private float _weight;

        private void Awake() {
            _animator = GetComponent<Animator>();
            _skills = GetComponentInParent<SkillSystem>();
        }

        private void Start() {
            CacheSpineBones();
        }

        /// <summary>
        /// Resolves the spine chain from the humanoid avatar. UpperChest is optional on many rigs, so
        /// whatever subset exists shares the corrective twist between them.
        /// </summary>
        private void CacheSpineBones() {
            if (_animator == null || !_animator.isHuman) return;

            AddBoneIfPresent(HumanBodyBones.Spine);
            AddBoneIfPresent(HumanBodyBones.Chest);
            AddBoneIfPresent(HumanBodyBones.UpperChest);
        }

        private void AddBoneIfPresent(HumanBodyBones bone) {
            Transform boneTransform = _animator.GetBoneTransform(bone);
            if (boneTransform == null) return;

            _spineBones.Add(boneTransform);
        }

        private void LateUpdate() {
            float targetWeight = IsAimingSkillActive() ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, targetWeight, blendSpeed * Time.deltaTime);

            ApplySpineTwist();
        }

        /// <summary>
        /// Yaws the spine chain after the animator has posed it, splitting the corrective angle
        /// evenly so the bend reads as one smooth torso turn rather than a kink at a single bone.
        /// </summary>
        private void ApplySpineTwist() {
            if (_weight <= 0.001f) return;
            if (_spineBones.Count == 0) return;

            float degreesPerBone = correctiveTwistDegrees * _weight / _spineBones.Count;
            foreach (Transform bone in _spineBones) {
                bone.Rotate(Vector3.up, degreesPerBone, Space.World);
            }
        }

        private void OnAnimatorIK(int layerIndex) {
            if (layerIndex != 0) return;
            if (_animator == null) return;

            if (_weight <= 0.001f) {
                _animator.SetLookAtWeight(0f);
                return;
            }

            _animator.SetLookAtWeight(_weight, 0f, headWeight, 0f, clampWeight);
            _animator.SetLookAtPosition(ResolveAimPoint());
        }

        /// <summary>
        /// True while the owning skill system is running an upper-body skill, which is the only time
        /// the corrective aim should fight the clip.
        /// </summary>
        private bool IsAimingSkillActive() {
            if (_skills == null) return false;
            if (_skills.ActiveSkill == null) return false;

            return _skills.ActiveSkill.Definition.bodyDomain == SkillBodyDomain.UpperBody;
        }

        /// <summary>
        /// Point ahead of the actor the head turns toward: actor facing rather than animator facing,
        /// so the correction tracks code-driven rotation instead of whatever the clip left behind.
        /// </summary>
        private Vector3 ResolveAimPoint() {
            Transform aimRoot = _skills != null ? _skills.transform : transform;

            return aimRoot.position + aimRoot.forward * aimDistance + Vector3.up * aimHeight;
        }
    }
}
