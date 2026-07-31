using UnityEngine;

namespace AlpineLib.Actors {
    /// <summary>
    /// Base class for behaviours that live on an actor and must stop running once that actor dies.
    /// </summary>
    /// <remarks>
    /// Death cleanup is decentralised by convention: nothing switches subsystems off from the outside.
    /// Each subsystem subscribes to its owner's <see cref="IMortal.OnDeath"/> and disables itself, so a
    /// new subsystem is dead-safe the moment it is written. Override <see cref="OnOwnerDeath"/> to run
    /// extra teardown (release targets, reset shader globals, stop coroutines) and call the base
    /// implementation unless the subsystem genuinely needs to keep updating after death.
    ///
    /// The owner is resolved from the same GameObject and is required: a subsystem on an object that
    /// is not an <see cref="IMortal"/> fails immediately rather than silently never cleaning up.
    /// Derived classes overriding <c>Start</c> or <c>OnDestroy</c> must call the base implementation,
    /// otherwise the subscription is never made or never released.
    /// </remarks>
    public abstract class ActorSubsystem : MonoBehaviour {
        /// <summary>
        /// The mortal this subsystem belongs to. Available from <c>Start</c> onwards.
        /// </summary>
        protected IMortal Owner { get; private set; }

        protected virtual void Start() {
            Owner = GetComponent<IMortal>();
            Owner.OnDeath += HandleOwnerDeath;
        }

        protected virtual void OnDestroy() {
            Owner.OnDeath -= HandleOwnerDeath;
        }

        private void HandleOwnerDeath() {
            OnOwnerDeath();
        }

        /// <summary>
        /// Called once when the owner dies. Disables the subsystem by default.
        /// </summary>
        protected virtual void OnOwnerDeath() => enabled = false;
    }
}
