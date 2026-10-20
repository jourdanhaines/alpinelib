using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>
    /// Eases one animator layer's weight toward a target, e.g. an upper-body hold layer that blends in
    /// when a prop is raised and out when it is put away.
    /// </summary>
    /// <remarks>
    /// Callers set <see cref="Target"/> and read <see cref="Weight"/>; the eased weight is the source of
    /// truth, so a procedural layer can scale itself by the same number the animator sees. Safe with no
    /// animator, an uninitialised one, or a layer index the controller does not have: the weight still
    /// eases, it simply reaches nothing.
    ///
    /// Runs at order 295, after owners that set <see cref="Target"/> from <c>Update</c> at 290 or
    /// earlier, so a target set this frame starts easing this frame. Disabling the component drops the
    /// layer to zero, so a disabled blend never leaves the layer stuck on.
    /// </remarks>
    [DefaultExecutionOrder(295)]
    public class AnimatorLayerBlend : MonoBehaviour {
        [Tooltip("Animator driven; found in children when left empty.")]
        [SerializeField] private Animator animator;

        [Tooltip("Index of the layer whose weight is eased.")]
        [SerializeField] private int layerIndex = 1;

        [Tooltip("Weight units per second while rising toward the target.")]
        [SerializeField] private float blendInSpeed = 6f;

        [Tooltip("Weight units per second while falling toward the target.")]
        [SerializeField] private float blendOutSpeed = 6f;

        private float _target;
        private float _weight;

        /// <summary>Weight the layer eases toward, clamped to [0, 1].</summary>
        public float Target {
            get => _target;
            set => _target = Mathf.Clamp01(value);
        }

        /// <summary>Current eased weight, the one written to the animator.</summary>
        public float Weight => _weight;

        /// <summary>Animator layer whose weight this blend drives.</summary>
        public int LayerIndex => layerIndex;

        /// <summary>Jumps target and weight to <paramref name="weight"/> this frame, without easing.</summary>
        public void Snap(float weight) {
            _target = Mathf.Clamp01(weight);
            _weight = _target;
            WriteWeight();
        }

        private void OnDisable() {
            _weight = 0f;
            WriteWeight();
        }

        private void Update() {
            float speed = _target >= _weight ? blendInSpeed : blendOutSpeed;
            _weight = Mathf.MoveTowards(_weight, _target, Mathf.Max(speed, 0f) * Time.deltaTime);
            WriteWeight();
        }

        private void WriteWeight() {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isInitialized) return;
            if (layerIndex < 0 || layerIndex >= animator.layerCount) return;

            animator.SetLayerWeight(layerIndex, _weight);
        }
    }
}
