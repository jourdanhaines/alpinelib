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
    /// the frame the brain actually changes.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class PossessionGate : MonoBehaviour {
        [Tooltip("Components that only run on the pawn this client drives. Author them disabled.")]
        [SerializeField] private Behaviour[] localOnly;

        private Actor _actor;
        private Controller _lastBrain;

        private void Awake() {
            _actor = GetComponent<Actor>();
        }

        private void Update() {
            Controller brain = _actor.Brain;

            if (ReferenceEquals(brain, _lastBrain)) return;

            _lastBrain = brain;
            SetLocalControlEnabled(brain != null && !(brain is NetController));
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
