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
    /// <see cref="SetId"/> instead. Zero is reserved for "world space" on the wire, so here it means
    /// <em>unassigned</em>: a carrier holding zero simply is not registered and waits, silently, for the
    /// id its game will give it — and <c>SetId(0)</c> withdraws one.
    /// </para>
    /// <para>
    /// <b>Only a registered carrier is a frame.</b> Registration is what makes an id resolvable, and a
    /// rider's state is only worth labelling with an id every peer can turn back into this object — so
    /// <see cref="IsRegistered"/> is the question the capture side asks, and an unregistered carrier
    /// means world space rather than deck-local metres under a label nobody can invert. Registration is
    /// retried every frame while it has not taken, so an id assigned a frame late, a scale a game fixes
    /// after enabling, or a duplicate whose holder is later destroyed all heal on their own instead of
    /// stranding this carrier's riders for the session.
    /// </para>
    /// <para>
    /// <b>A game must stop carrying a rider it may not report.</b> The library owns the reporting and not
    /// the carry, so it cannot enforce this: a game that goes on physically moving a body with a carrier
    /// that never registers moves it at the deck's speed while its owner has nothing truthful left to
    /// send, and the pawn freezes for every observer where the server last saw it — see
    /// <see cref="NetActorSync"/>'s withheld-update note. Treat <see cref="IsRegistered"/> as part of the
    /// question "is this a platform", not only as part of "what do I call it".
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
        /// <summary>
        /// How far each axis of the lossy scale may sit from one and still count as unit scale. Wide
        /// enough to absorb the float error of a deep transform hierarchy, narrow enough that an authored
        /// 0.99 is not mistaken for it.
        /// </summary>
        public const float ScaleTolerance = 1e-3f;

        /// <summary>
        /// How long a game's <see cref="INetCarrierSource"/> must see a new carrier before it may report
        /// the change. The hysteresis every source owes the replication side; see that interface.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three ticks of a thirty-hertz server, and deliberately not one tick more. What a dwell has to
        /// clear is the server's <em>sustained</em> rate of unmeasured moves, and that floor is a count of
        /// ticks rather than a ratio of seconds: a source's changes land on send ticks, so what matters is
        /// that a budget's worth of dwells outlasts the window — <c>CarrierSwitchCooldownTicks /
        /// (ServerTickRate × MaxCarrierSwitchesPerWindow)</c>, 8 / 90 = 0.089 s. Three ticks clears that
        /// by about a tenth and no more, so the dwell and the budget are one decision: lowering the
        /// budget means lengthening this first; see <c>MovementValidator.MaxCarrierSwitchesPerWindow</c>.
        /// It cannot go to zero either: a hop or a step over a rail breaks ground contact for a handful
        /// of frames without the rider leaving the deck, and this outlasts that at sixty frames a second.
        /// </para>
        /// <para>
        /// Every tick of dwell is paid for by a rider whose two frames are not moving together — see
        /// <see cref="INetCarrierSource"/> — which is why the constant sits at the bottom of the range
        /// rather than in the middle of it.
        /// </para>
        /// </remarks>
        public const float SourceHysteresisSeconds = 0.1f;

        [Tooltip("Session-wide id riders name in their replicated state. Must match on every peer; leave at zero for a carrier whose id is assigned at runtime.")]
        [SerializeField] private ushort carrierId;

        private Vector3 _previousPosition;
        private bool _hasPreviousPosition;
        private bool _isRegistered;
        private bool _hasReportedScale;
        private bool _hasReportedDuplicate;

        /// <summary>The id riders name in their replicated state.</summary>
        public ushort CarrierId => carrierId;

        /// <summary>
        /// True only while <see cref="NetCarrierRegistry"/> resolves this carrier's id to this very
        /// object — the same question every reader of an inbound state asks.
        /// </summary>
        /// <remarks>
        /// Asked of the registry rather than answered from a cached flag so that the side that
        /// <em>labels</em> a state and the side that <em>resolves</em> one cannot disagree. They did:
        /// a carrier still holding the unassigned id, or one the registry turned down for a duplicate id
        /// or a non-unit scale, was happily handed out by a game's carrier source, and the deck-local
        /// metres it produced went onto the wire as world space or under an id nothing answers to.
        /// </remarks>
        public bool IsRegistered =>
            _isRegistered
            && carrierId != PawnState.WorldCarrierId
            && NetCarrierRegistry.TryResolve(carrierId, out NetCarrier resolved)
            && ReferenceEquals(resolved, this);

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
        /// it registers, and setting zero withdraws it entirely.
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
            _hasReportedScale = false;
            _hasReportedDuplicate = false;
            Velocity = Vector3.zero;
            Register();
        }

        private void OnDisable() {
            if (!_isRegistered) return;

            NetCarrierRegistry.Unregister(this);
            _isRegistered = false;
        }

        private void Update() {
            Register();

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
        /// Publishes this carrier under its current id, once it has one, if its scale allows and if no
        /// other carrier already holds that id. Called again every frame until it takes.
        /// </summary>
        /// <remarks>
        /// Zero is the unassigned state and is passed over in silence, not reported: a carrier spawned
        /// from a prefab enables before whatever builds it hands out ids, so an error here would fire
        /// once per car on every scene load and mean nothing. Retrying is what turns every refusal into
        /// a delay: an id arrives, a game fixes a scale, a duplicate's holder is destroyed, and this
        /// carrier picks the id up on the next frame instead of leaving its riders unresolvable for the
        /// rest of the session. Each refusal is reported once, not once a frame.
        /// </remarks>
        private void Register() {
            if (_isRegistered) return;
            if (carrierId == PawnState.WorldCarrierId) return;

            if (!HasUnitScale()) return;
            if (IsIdHeldByAnother()) return;

            _isRegistered = NetCarrierRegistry.Register(this);
        }

        /// <summary>
        /// Whether a different live carrier already answers to this id, reporting the clash once.
        /// </summary>
        /// <remarks>
        /// Detected here rather than left to <see cref="NetCarrierRegistry.Register"/> because
        /// registration is retried every frame, and the registry's own error would then arrive once a
        /// frame for the whole session.
        /// </remarks>
        private bool IsIdHeldByAnother() {
            if (!NetCarrierRegistry.TryResolve(carrierId, out NetCarrier holder) || holder == this) {
                _hasReportedDuplicate = false;
                return false;
            }

            if (_hasReportedDuplicate) return true;

            _hasReportedDuplicate = true;
            Debug.LogError($"NetCarrier::IsIdHeldByAnother->Carrier id {carrierId} is already held by '{holder.name}'; '{name}' stays unregistered and its riders replicate in world space until that id is free.");
            return true;
        }

        /// <summary>
        /// Whether this object's world scale is close enough to one to be a frame, reporting it when it
        /// is not.
        /// </summary>
        /// <remarks>
        /// Refusing rather than registering anyway is the safer failure: a scaled frame replicates
        /// positions in scaled units and velocities in world ones, so the server measures a stretched
        /// displacement against an unscaled gait ceiling and clamps an honest walk every tick — a
        /// carrier that rubber-bands its riders is much harder to recognise than one whose riders stay
        /// in world space with an error in the log.
        ///
        /// Only asked while unregistered, so a carrier <em>scaled after</em> it registers — an animated
        /// lift, a tween — keeps its registration and does produce the scaled coordinates this refuses.
        /// A carrier's scale is expected to be authored once and left alone.
        /// </remarks>
        private bool HasUnitScale() {
            Vector3 scale = transform.lossyScale;

            if (Mathf.Abs(scale.x - 1f) <= ScaleTolerance
                && Mathf.Abs(scale.y - 1f) <= ScaleTolerance
                && Mathf.Abs(scale.z - 1f) <= ScaleTolerance) {
                _hasReportedScale = false;
                return true;
            }

            if (_hasReportedScale) return false;

            _hasReportedScale = true;
            Debug.LogError($"NetCarrier::HasUnitScale->{name} has a lossy scale of {scale}; carrier frames are unit-scale only and this carrier will not be registered until its scale is one.");
            return false;
        }
    }
}
