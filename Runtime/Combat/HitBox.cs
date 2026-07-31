using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// The business end of a weapon or limb: a kinematic trigger collider that stays disabled until
    /// the attack's damage window opens, then reports every <see cref="HurtBox"/> it overlaps back to
    /// the <see cref="CombatSystem"/> that drives it.
    /// </summary>
    /// <remarks>
    /// Targets are de-duplicated for the duration of one activation, so a swing never hits the same
    /// hurt box twice. With <c>isSingleHit</c> the whole swing stops after the first contact, which is
    /// the usual choice for punches and bites; clear it for sweeping attacks meant to catch a crowd.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public class HitBox : MonoBehaviour {
        [Tooltip("Stop after the first target this swing connects with")]
        [SerializeField] private bool isSingleHit = true;

        private CombatSystem _combat;
        private Collider _collider;
        private bool _hasConnected;
        private readonly HashSet<HurtBox> _hitTargets = new();

        private void Awake() {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
            _collider.enabled = false;

            var rigidBody = GetComponent<Rigidbody>();
            if (rigidBody == null)
                rigidBody = gameObject.AddComponent<Rigidbody>();
            rigidBody.isKinematic = true;
        }

        /// <summary>
        /// Tells this hit box which combat system to report contacts to. Called by that system on start.
        /// </summary>
        public void Init(CombatSystem combat) {
            _combat = combat;
        }

        /// <summary>
        /// Opens the damage window: clears the already-hit set and enables the trigger.
        /// </summary>
        public void Activate() {
            _hasConnected = false;
            _hitTargets.Clear();
            _collider.enabled = true;
        }

        /// <summary>
        /// Closes the damage window.
        /// </summary>
        public void Deactivate() {
            _collider.enabled = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            if (_collider == null || !_collider.enabled) return;

            Gizmos.color = Color.red;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (_collider is SphereCollider sphere)
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            else if (_collider is CapsuleCollider capsule)
                DrawWireCapsuleGizmo(capsule);
            else if (_collider is BoxCollider box)
                Gizmos.DrawWireCube(box.center, box.size);
        }

        private static void DrawWireCapsuleGizmo(CapsuleCollider c) {
            float r = c.radius;
            float h = Mathf.Max(0f, c.height * 0.5f - r);
            Vector3 up = c.direction switch {
                0 => Vector3.right,
                2 => Vector3.forward,
                _ => Vector3.up
            };
            Gizmos.DrawWireSphere(c.center + up * h, r);
            Gizmos.DrawWireSphere(c.center - up * h, r);
        }
#endif

        private void OnTriggerStay(Collider other) {
            if (_hasConnected) return;

            var hurtBox = other.GetComponent<HurtBox>();
            if (hurtBox == null) return;
            if (_hitTargets.Contains(hurtBox)) return;

            _hitTargets.Add(hurtBox);

            if (isSingleHit)
                _hasConnected = true;

            _combat.OnHitBoxContact(hurtBox);
        }
    }
}
