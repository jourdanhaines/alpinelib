using System;
using UnityEngine;

namespace AlpineLib.Perception {
    /// <summary>
    /// Emits a footstep noise whenever a foot plants, detected from toe bone height rather than
    /// animation events, so it works with any clip set without authoring per-clip events.
    /// </summary>
    /// <remarks>
    /// Requires a humanoid avatar: the toe bones are resolved through
    /// <c>Animator.GetBoneTransform</c>, so generic rigs need a different implementation. Ground
    /// height is taken from this transform's Y each frame, which assumes the actor is standing on
    /// roughly level ground beneath its own origin.
    /// </remarks>
    public class NoiseEmitterFootstep : MonoBehaviour {
        [SerializeField] private float groundThreshold = 0.1f;
        [SerializeField] private float noiseRadius = 10f;

        /// <summary>
        /// Overrides the serialized noise radius when set. Evaluated on every foot plant, so a game
        /// can feed it from a stat sheet or a gait system.
        /// </summary>
        public Func<float> NoiseRadiusProvider;

        /// <summary>
        /// How far a footstep carries. Reads <see cref="NoiseRadiusProvider"/> when one is set.
        /// </summary>
        public float NoiseRadius {
            get => NoiseRadiusProvider != null ? NoiseRadiusProvider() : noiseRadius;
            set => noiseRadius = value;
        }

        private Animator _animator;
        private Transform _leftFoot;
        private Transform _rightFoot;
        private bool _leftWasGrounded;
        private bool _rightWasGrounded;
        private float _baseY;

        private void Start() {
            _animator = GetComponentInChildren<Animator>();
            _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
            _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightToes);
        }

        private void Update() {
            _baseY = transform.position.y;

            CheckFoot(_leftFoot, ref _leftWasGrounded);
            CheckFoot(_rightFoot, ref _rightWasGrounded);
        }

        private void CheckFoot(Transform foot, ref bool wasGrounded) {
            if (foot == null) return;

            bool isGrounded = foot.position.y - _baseY <= groundThreshold;

            if (isGrounded && !wasGrounded)
                NoiseEmitter.Emit(gameObject, transform.position, NoiseRadius);

            wasGrounded = isGrounded;
        }
    }
}
