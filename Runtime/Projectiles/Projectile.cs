using AlpineLib.Combat;
using UnityEngine;

namespace AlpineLib.Projectiles {
    /// <summary>
    /// A fire-and-forget damage carrier: once launched it translates along a fixed direction at a
    /// fixed speed until its lifetime runs out or it overlaps a <see cref="HurtBox"/> that does not
    /// belong to its owner, at which point it applies its <see cref="DamagePacket"/> and destroys
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Movement is pure translation of the transform, not rigidbody physics. The attached
    /// <see cref="Rigidbody"/> exists only so Unity generates trigger callbacks against static
    /// colliders; <see cref="Awake"/> forces it kinematic and gravity-free, and forces the collider
    /// to be a trigger, so a mis-authored prefab cannot turn a projectile into a physics object that
    /// shoves the world around.
    /// </para>
    /// <para>
    /// v1 deliberately has no wall collision: the projectile passes through level geometry and only
    /// reacts to hurt boxes. Terrain stops are the intended extension point — either give geometry
    /// hurt-box-less trigger colliders and handle them in <see cref="OnTriggerEnter"/>, or sweep a
    /// raycast between the previous and current position in <see cref="Update"/>. Until then, keep
    /// lifetimes short so strays are reclaimed promptly.
    /// </para>
    /// <para>
    /// A projectile is inert before <see cref="Launch"/>: its trigger is disabled and it does not
    /// move, so a prefab dropped into a scene by hand does nothing until something fires it.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour {
        private Collider _collider;
        private Vector3 _direction;
        private float _speed;
        private float _remainingLifetime;
        private DamagePacket _packet;
        private GameObject _owner;
        private bool _isLaunched;

        /// <summary>
        /// Places the projectile at <paramref name="origin"/> facing <paramref name="direction"/>,
        /// arms its trigger, and starts it travelling.
        /// </summary>
        /// <param name="origin">World position the projectile starts from, typically a muzzle socket.</param>
        /// <param name="direction">
        /// World-space travel direction. Normalized internally, so callers may pass an unnormalized
        /// aim vector.
        /// </param>
        /// <param name="speed">Travel speed in units per second.</param>
        /// <param name="lifetime">Seconds before the projectile destroys itself if it hits nothing.</param>
        /// <param name="packet">Damage applied to the first valid hurt box this projectile overlaps.</param>
        /// <param name="owner">
        /// The firing actor's game object. Any hurt box sharing a transform root with it is ignored,
        /// which is what keeps a caster from shooting themselves in the foot. May be null for
        /// world-owned hazards, in which case nothing is filtered out.
        /// </param>
        /// <remarks>
        /// A zero-length <paramref name="direction"/> falls back to the projectile's current forward
        /// axis rather than producing a stationary projectile that lingers for its whole lifetime.
        /// Re-launching a live projectile simply overwrites its state, which is what pooled reuse
        /// needs.
        /// </remarks>
        public void Launch(Vector3 origin, Vector3 direction, float speed, float lifetime, in DamagePacket packet, GameObject owner) {
            _direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
            _speed = speed;
            _remainingLifetime = lifetime;
            _packet = packet;
            _owner = owner;
            _isLaunched = true;

            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(_direction, Vector3.up));

            if (_collider != null)
                _collider.enabled = true;
        }

        private void Awake() {
            var rigidBody = GetComponent<Rigidbody>();
            rigidBody.isKinematic = true;
            rigidBody.useGravity = false;

            _collider = GetComponent<Collider>();
            if (_collider == null) return;

            _collider.isTrigger = true;
            _collider.enabled = false;
        }

        private void Update() {
            if (!_isLaunched) return;

            transform.position += _direction * (_speed * Time.deltaTime);

            _remainingLifetime -= Time.deltaTime;
            if (_remainingLifetime > 0f) return;

            Destroy(gameObject);
        }

        private void OnTriggerEnter(Collider other) {
            if (!_isLaunched) return;

            var hurtBox = other.GetComponentInParent<HurtBox>();
            if (hurtBox == null) return;
            if (IsOwnedByShooter(hurtBox)) return;

            _isLaunched = false;
            DamageResolver.Apply(_packet, hurtBox);
            Destroy(gameObject);
        }

        private bool IsOwnedByShooter(HurtBox hurtBox) {
            if (_owner == null) return false;

            return hurtBox.transform.root == _owner.transform.root;
        }
    }
}
