using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// One layer of procedural motion laid over the animated pose: a look that bends the neck, a lean,
    /// a recoil. Run by the <see cref="PoseModifierStack"/> on the same actor, never on its own.
    /// </summary>
    /// <remarks>
    /// A modifier only ever adds to the pose it is handed, through <see cref="PoseBones"/>, so layers
    /// compose in <see cref="Order"/> and none of them needs to know which others exist.
    /// </remarks>
    public abstract class PoseModifier : MonoBehaviour {
        [Tooltip("Position in the stack; lower runs first, so later layers see its result.")]
        [SerializeField] private int order;

        [Tooltip("How much of this layer reaches the pose.")]
        [Range(0f, 1f)]
        [SerializeField] private float weight = 1f;

        public int Order => order;

        public float Weight {
            get => weight;
            set => weight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Called once before the first <see cref="Apply"/>, with the skeleton the layer will write to.
        /// </summary>
        public abstract void Bind(Transform actorRoot, PoseBones bones);

        /// <summary>
        /// Adds this layer's motion to the pose the animator and any earlier layer left behind.
        /// </summary>
        public abstract void Apply(PoseBones bones, float deltaTime);
    }
}
