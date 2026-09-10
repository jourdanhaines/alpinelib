using AlpineLib.Netcode.Replication;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// Marks a moving object that pawns may stand on and be replicated relative to, and gives it the
    /// session-wide id their states name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A carrier is anything large and fast enough that replicating its riders in world space stops
    /// working: a train, a ship, a lift. A pawn walking such a deck moves a metre a second of its own
    /// accord and forty through the world, and world-space replication makes the server judge the forty,
    /// the interpolator smooth the forty, and every packet loss show as a forty-metre slide. Naming the
    /// carrier instead lets the rider replicate the one metre it is actually responsible for; see
    /// <see cref="PawnState.CarrierId"/> for the frame contract and
    /// <see cref="NetCarrierFrame"/> for the conversion.
    /// </para>
    /// <para>
    /// Ids are the game's to assign and must agree across every peer in a session, because they travel on
    /// the wire in place of the object itself. Authoring one in the inspector suits a carrier that is part
    /// of the scene; a carrier spawned from a server-driven manifest takes its id through
    /// <see cref="SetId"/> instead. Zero is reserved for "world space" and is never a valid carrier.
    /// </para>
    /// <para>
    /// The velocity published here is measured, not authored: one frame's world-space position delta
    /// divided by the frame time, taken at <see cref="NetExecutionOrder.Carriers"/> so it is already this
    /// frame's value by the time a rider's sync reads it. Measuring rather than asking the carrier's own
    /// movement code keeps this component usable by any mover — a spline-driven train, an animated lift,
    /// a physics body — without a common interface for them to implement.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(NetExecutionOrder.Carriers)]
    public class NetCarrier : MonoBehaviour {
        [Tooltip("Session-wide id riders name in their replicated state. Must match on every peer; zero is reserved for world space.")]
        [SerializeField] private ushort carrierId;

        private Vector3 _previousPosition;
        private bool _hasPreviousPosition;
        private bool _isRegistered;

        /// <summary>The id riders name in their replicated state.</summary>
        public ushort CarrierId => carrierId;

        /// <summary>
        /// This carrier's own world velocity in metres per second, from the last frame's motion. Zero on
        /// the first frame after it is enabled, when there is no previous pose to difference against.
        /// </summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>
        /// Assigns the id at runtime, moving the registration with it. Used by whatever hands out ids for
        /// carriers that are spawned rather than authored.
        /// </summary>
        /// <remarks>
        /// Re-registering rather than merely storing matters because the registry is keyed by id: leaving
        /// the old key in place would resolve every rider still naming it to a carrier that no longer
        /// answers to that number. A carrier that is not currently enabled simply keeps the new id until
        /// it registers.
        /// </remarks>
        public void SetId(ushort id) {
            if (carrierId == id) return;

            if (_isRegistered) {
                NetCarrierRegistry.Unregister(this);
                _isRegistered = false;
            }

            carrierId = id;

            if (!isActiveAndEnabled) return;

            Register();
        }

        private void OnEnable() {
            _hasPreviousPosition = false;
            Velocity = Vector3.zero;
            Register();
        }

        private void OnDisable() {
            if (!_isRegistered) return;

            NetCarrierRegistry.Unregister(this);
            _isRegistered = false;
        }

        private void Update() {
            Vector3 position = transform.position;

            if (!_hasPreviousPosition || Time.deltaTime <= 0f) {
                _previousPosition = position;
                _hasPreviousPosition = true;
                Velocity = Vector3.zero;
                return;
            }

            Velocity = (position - _previousPosition) / Time.deltaTime;
            _previousPosition = position;
        }

        /// <summary>
        /// Publishes this carrier under its current id, refusing the reserved zero.
        /// </summary>
        /// <remarks>
        /// An unassigned id is reported rather than registered because zero means "world space" on the
        /// wire: registering under it would let a rider ask for the world and be handed a train.
        /// </remarks>
        private void Register() {
            if (carrierId == PawnState.WorldCarrierId) {
                Debug.LogError($"NetCarrier::Register->{name} has no carrier id; zero is reserved for world space and this carrier will not be resolvable.");
                return;
            }

            _isRegistered = NetCarrierRegistry.Register(this);
        }
    }
}
