using UnityEngine;

namespace AlpineLib.Skills {
    /// <summary>
    /// Pulls the torso and head toward the actor's facing while an upper-body skill is active, using
    /// the humanoid animator's look-at IK.
    /// </summary>
    /// <remarks>
    /// Upper-body skill clips play through an avatar mask that hands the hips to the locomotion
    /// layer, so any torso turn the clip authored into the hips is discarded and the casting arm
    /// lands wherever the remaining spine pose points — usually off to one side of where the actor
    /// is actually facing. Rather than demanding perfectly square-on source clips, this component
    /// bends the spine chain toward a point ahead of the actor every frame the skill runs, which
    /// centres the pose no matter how the clip was authored.
    ///
    /// The component must live on the same GameObject as the <see cref="Animator"/> —
    /// <c>OnAnimatorIK</c> only fires there — and the animator's base layer must have its IK pass
    /// enabled, or the callback never runs and the component is a silent no-op.
    /// <see cref="SkillSystem"/> adds one to its actor's animator automatically when none is present,
    /// so hand-placing it on a prefab is only needed to override the default weights.
    /// </remarks>
    [DisallowMultipleComponent]
    public class UpperBodyAim : MonoBehaviour {
        [Tooltip("How strongly the spine chain bends toward the aim point.")]
        [Range(0f, 1f)]
        [SerializeField] private float bodyWeight = 0.5f;

        [Tooltip("How strongly the head turns toward the aim point.")]
        [Range(0f, 1f)]
        [SerializeField] private float headWeight = 0.3f;

        [Tooltip("How much of the motion is clamped; higher values keep the pose closer to the clip.")]
        [Range(0f, 1f)]
        [SerializeField] private float clampWeight = 0.5f;

        [Tooltip("Metres ahead of the actor the aim point sits.")]
        [SerializeField] private float aimDistance = 10f;

        [Tooltip("Height of the aim point above the actor's feet, roughly chest to eye level.")]
        [SerializeField] private float aimHeight = 1.5f;

        [Tooltip("Blend-in and blend-out speed, in weight units per second.")]
        [SerializeField] private float blendSpeed = 8f;

        private Animator _animator;
        private SkillSystem _skills;
        private float _weight;

        private void Awake() {
            _animator = GetComponent<Animator>();
            _skills = GetComponentInParent<SkillSystem>();
        }

        private void OnAnimatorIK(int layerIndex) {
            if (layerIndex != 0) return;
            if (_animator == null) return;

            float targetWeight = IsAimingSkillActive() ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, targetWeight, blendSpeed * Time.deltaTime);

            if (_weight <= 0.001f) {
                _animator.SetLookAtWeight(0f);
                return;
            }

            _animator.SetLookAtWeight(_weight, bodyWeight, headWeight, 0f, clampWeight);
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
        /// Point ahead of the actor the torso bends toward: actor facing rather than animator facing,
        /// so the correction tracks code-driven rotation instead of whatever the clip left behind.
        /// </summary>
        private Vector3 ResolveAimPoint() {
            Transform aimRoot = _skills != null ? _skills.transform : transform;

            return aimRoot.position + aimRoot.forward * aimDistance + Vector3.up * aimHeight;
        }
    }
}
