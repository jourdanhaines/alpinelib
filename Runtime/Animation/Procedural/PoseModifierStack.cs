using System.Collections.Generic;
using AlpineLib.Actors;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Runs an actor's <see cref="PoseModifier"/> layers over the animated pose, once per frame and in
    /// order.
    /// </summary>
    /// <remarks>
    /// One runner rather than a <c>LateUpdate</c> per layer, so the order between layers is authored
    /// instead of left to the script execution order, and so the bookkeeping that keeps additive writes
    /// additive — see <see cref="PoseBones"/> — brackets all of them at once. Runs after the actor's own
    /// <c>LateUpdate</c>; the animator has posed the skeleton well before either.
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(ActorExecutionOrder.PoseModifiers)]
    public class PoseModifierStack : MonoBehaviour {
        private readonly List<PoseModifier> _modifiers = new List<PoseModifier>();
        private PoseBones _bones;
        private bool _isBound;

        private void Start() {
            Bind();
        }

        private void LateUpdate() {
            Apply(Time.deltaTime);
        }

        /// <summary>
        /// Lays every enabled layer over the current pose. Public so a gate can pose a body by hand,
        /// the way <see cref="Actor.Simulate"/> lets one step it.
        /// </summary>
        public void Apply(float deltaTime) {
            Bind();

            if (!_isBound || !_bones.IsHumanoid) return;

            _bones.BeginFrame();

            foreach (PoseModifier modifier in _modifiers) {
                if (!modifier.isActiveAndEnabled || modifier.Weight <= 0f) continue;

                modifier.Apply(_bones, deltaTime);
            }

            _bones.EndFrame();
        }

        // Waits for an initialised animator: before that it maps no bones, and layers bound against
        // nothing would stay bound to nothing.
        private void Bind() {
            if (_isBound) return;

            Animator animator = GetComponentInChildren<Animator>();
            if (animator != null && !animator.isInitialized) return;

            _isBound = true;
            _bones = new PoseBones(animator);

            if (!_bones.IsHumanoid) return;

            GetComponentsInChildren(true, _modifiers);
            _modifiers.Sort(CompareOrder);

            foreach (PoseModifier modifier in _modifiers) {
                modifier.Bind(transform, _bones);
            }
        }

        private static int CompareOrder(PoseModifier left, PoseModifier right) {
            return left.Order.CompareTo(right.Order);
        }
    }
}
