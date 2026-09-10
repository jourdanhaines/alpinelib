using AlpineLib.Actors;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// Switches on the components that only make sense on the pawn this client is actually driving, and
    /// leaves them off on everybody else's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One prefab serves both the local player and every remote one, so the prefab cannot decide which it
    /// is — possession does. A pawn taken by a local brain gets its input reader, its controller and its
    /// first-person body switching; a pawn taken by <see cref="NetController"/> is somebody else's and
    /// keeps all three off, which is why a remote pawn never hides its body when the local player looks
    /// through their own eyes.
    /// </para>
    /// <para>
    /// The gated components are authored <i>disabled</i> on the prefab and only ever switched on here.
    /// That direction matters for the input reader above all: the action asset is shared between every
    /// reader in the process, so a remote pawn's reader being disabled would call <c>Disable</c> on the
    /// map the local player is reading and kill input for everyone. A reader that is never enabled in the
    /// first place cannot do that.
    /// </para>
    /// <para>
    /// Possession is polled rather than announced because <see cref="Actor"/> exposes the brain that holds
    /// it as plain state; the check is a single reference comparison and the work behind it runs only on
    /// the frame the answer — local or not — actually changes. Left at Unity's default execution order on
    /// purpose: the gate belongs ahead of the pawn drivers, which are pinned behind it at
    /// <see cref="NetExecutionOrder.PawnDrivers"/>.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class PossessionGate : MonoBehaviour {
        [Tooltip("Components that only run on the pawn this client drives. Author them disabled.")]
        [SerializeField] private Behaviour[] localOnly;

        private Actor _actor;
        private Controller _lastBrain;
        private bool? _appliedLocalControl;

        /// <summary>
        /// True when the brain that has just taken this actor is the local player's.
        /// </summary>
        /// <remarks>
        /// Any brain that is not the network's, by default: a pawn nobody remote is driving is the one
        /// this client drives. A game that also puts an AI or cutscene brain on the same prefab narrows
        /// this rather than reimplementing the gate — the default would switch the player's input reader
        /// on for that brain too, since an AI steers through intents exactly as a player does.
        /// </remarks>
        protected virtual bool IsLocalBrain(Controller brain) {
            return brain != null && !(brain is NetController);
        }

        /// <remarks>Overrides must call <c>base.Awake()</c> or the actor behind the gate is never found.</remarks>
        protected virtual void Awake() {
            _actor = GetComponent<Actor>();

            if (localOnly != null && localOnly.Length > 0) return;

            Debug.LogWarning($"PossessionGate::Awake->{name} gates nothing; author the local-only components onto the prefab.");
        }

        /// <remarks>Overrides must call <c>base.Update()</c> or possession stops switching anything.</remarks>
        protected virtual void Update() {
            Controller brain = _actor.Brain;

            if (ReferenceEquals(brain, _lastBrain)) return;

            _lastBrain = brain;
            ApplyLocalControl(IsLocalBrain(brain));
        }

        /// <summary>
        /// Switches the gated components only when the local/remote answer changes, not on every
        /// hand-over.
        /// </summary>
        /// <remarks>
        /// A pawn changes brains during ordinary play — a walking brain gives the body to the one that
        /// drives a lever and takes it back on release — and both of those are local. Re-enabling the set
        /// on each hand-over would switch the walking brain back on underneath the lever brain that had
        /// just stood it down for itself. Only a move between local and remote is this gate's business.
        /// </remarks>
        private void ApplyLocalControl(bool isEnabled) {
            if (_appliedLocalControl == isEnabled) return;

            _appliedLocalControl = isEnabled;
            SetLocalControlEnabled(isEnabled);
        }

        /// <summary>
        /// Enables the gated components in the order they were authored and disables them in the reverse
        /// one, so a component always comes up after whatever it reads and goes down before it.
        /// </summary>
        private void SetLocalControlEnabled(bool isEnabled) {
            if (localOnly == null) return;

            if (isEnabled) {
                EnableInAuthoredOrder();
                return;
            }

            DisableInReverseOrder();
        }

        private void EnableInAuthoredOrder() {
            for (int index = 0; index < localOnly.Length; index++) {
                SetBehaviourEnabled(localOnly[index], true);
            }
        }

        private void DisableInReverseOrder() {
            for (int index = localOnly.Length - 1; index >= 0; index--) {
                SetBehaviourEnabled(localOnly[index], false);
            }
        }

        private void SetBehaviourEnabled(Behaviour behaviour, bool isEnabled) {
            if (behaviour == null) return;

            behaviour.enabled = isEnabled;
        }
    }
}
