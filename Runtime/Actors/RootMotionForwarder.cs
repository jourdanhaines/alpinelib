using UnityEngine;

namespace AlpineLib.Actors {
    /// <summary>
    /// Relays root motion from an animator on a child object up to the <see cref="Actor"/> on the
    /// parent, which owns the character controller. Root motion is skipped while any
    /// <see cref="IRootMotionSuppressor"/> above this object is driving the actor itself.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class RootMotionForwarder : MonoBehaviour {
        private Actor _actor;
        private Animator _animator;
        private IRootMotionSuppressor[] _suppressors;

        private void Start() {
            _actor = GetComponentInParent<Actor>();
            _animator = GetComponent<Animator>();
            _suppressors = GetComponentsInParent<IRootMotionSuppressor>(true);
        }

        private void OnAnimatorMove() {
            if (!_actor.IsAlive) return;

            foreach (var suppressor in _suppressors) {
                if (suppressor.IsSuppressingRootMotion) return;
            }

            _actor.ApplyRootMotion(_animator.deltaPosition);
        }
    }
}
