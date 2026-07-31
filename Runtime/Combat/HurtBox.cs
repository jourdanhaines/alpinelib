using AlpineLib.Actors;
using AlpineLib.Body;
using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// Tags a trigger collider as a damageable region of one body, so an attack that overlaps it
    /// knows which body part it struck and who it belongs to.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HurtBox : MonoBehaviour {
        [Tooltip("Body part this collider stands in for")]
        [SerializeField] private BodyPartDefinition bodyPart;

        /// <summary>
        /// Body part this collider stands in for.
        /// </summary>
        public BodyPartDefinition BodyPart => bodyPart;

        /// <summary>
        /// The owning mortal, or the hierarchy root when nothing above this collider is mortal.
        /// Attackers compare it against their own root to filter out hits on themselves.
        /// </summary>
        public Component Owner { get; private set; }

        private Collider _collider;
        private float _hitFlashTime;
        private const float HitFlashDuration = 0.5f;

        private void Awake() {
            Owner = ResolveOwner();
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
        }

        /// <summary>
        /// Lights this hurt box up in the scene view for half a second. Called when a hit lands.
        /// </summary>
        public void Flash() {
            _hitFlashTime = Time.time;
        }

        private Component ResolveOwner() {
            if (GetComponentInParent<IMortal>() is Component mortalComponent) return mortalComponent;

            return transform.root;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            if (Time.time - _hitFlashTime > HitFlashDuration) return;

            var col = _collider != null ? _collider : GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = Color.green;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (col is SphereCollider sphere)
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            else if (col is CapsuleCollider capsule)
                DrawWireCapsuleGizmo(capsule);
            else if (col is BoxCollider box)
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
    }
}
