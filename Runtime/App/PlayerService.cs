using System;
using AlpineLib.Actors;
using AlpineLib.DI;
using UnityEngine;

namespace AlpineLib.App {
    /// <summary>
    /// Tracks the actor the local player is driving and announces it the moment it exists.
    /// </summary>
    /// <remarks>
    /// Declared in the same file as its default implementation, matching the library's other service
    /// files. Deliberately thin: it is the seam a spawn lands on, whoever performs it. Offline the
    /// scene hands over the actor it just created; in a session the spawner does, and everything that
    /// cares — camera framing, HUD, menus — keeps watching <see cref="OnPlayerSpawned"/> rather than
    /// the object that happened to spawn the player.
    /// </remarks>
    public interface IPlayerService : IDependencyProvider {
        /// <summary>
        /// Actor the local player is driving, or null before one has been spawned.
        /// </summary>
        Actor Player { get; }

        /// <summary>
        /// Raised after the local player's actor has been possessed by its controller.
        /// </summary>
        event Action<Actor> OnPlayerSpawned;
    }

    /// <summary>
    /// App-root resident implementation of <see cref="IPlayerService"/>.
    /// </summary>
    /// <remarks>
    /// Registers itself with the injector in <c>Awake</c> instead of relying on the injector's scene
    /// sweep, because a game composes this service onto the app root at runtime and so it is never
    /// present during the sweep that follows a scene load.
    /// </remarks>
    public class PlayerService : MonoBehaviour, IPlayerService {
        /// <inheritdoc />
        public Actor Player { get; protected set; }

        /// <inheritdoc />
        public event Action<Actor> OnPlayerSpawned;

        /// <remarks>
        /// Declared here rather than on <see cref="IPlayerService"/> because the injector reflects over
        /// the concrete type when registering a provider; putting it on the interface would only oblige
        /// every consumer to look at a method none of them may call.
        /// </remarks>
        [Provide]
        public virtual IPlayerService ProvidePlayerService() {
            return this;
        }

        /// <summary>
        /// Takes ownership of a freshly spawned actor: has the controller riding on it possess it, and
        /// announces the result.
        /// </summary>
        /// <remarks>
        /// Possession is done here rather than by the spawning object because the actor and the brain
        /// that drives it are a pair the rest of the game only ever meets through this service. Not
        /// part of <see cref="IPlayerService"/>: only whoever composed this service may hand it a
        /// player, while everyone else reads <see cref="Player"/> and follows the event.
        /// </remarks>
        public virtual void SetPlayer(Actor player) {
            if (player == null) {
                Debug.LogError("PlayerService::SetPlayer->No player actor to take ownership of.");
                return;
            }

            SetPlayer(player, player.GetComponent<Controller>());
        }

        /// <summary>
        /// Takes ownership of a freshly spawned actor and names the brain that is to drive it.
        /// </summary>
        /// <remarks>
        /// The explicit overload exists because a networked pawn carries more than one
        /// <see cref="Controller"/> — the player's and the network's, since one prefab serves both a
        /// local and a remote pawn — and which of them takes the body is exactly what the spawner has
        /// just decided from ownership. Letting this service pick by component order would make that
        /// decision depend on the order components happen to sit in the prefab.
        /// </remarks>
        public virtual void SetPlayer(Actor player, Controller controller) {
            if (player == null) {
                Debug.LogError("PlayerService::SetPlayer->No player actor to take ownership of.");
                return;
            }

            if (controller == null) {
                Debug.LogError($"PlayerService::SetPlayer->{player.name} was handed no Controller.");
                return;
            }

            Player = player;
            controller.Possess(player);

            RaisePlayerSpawned(player);
        }

        /// <summary>
        /// Announces the player to everyone following the service. For subclasses that take a player
        /// over by a route of their own.
        /// </summary>
        protected void RaisePlayerSpawned(Actor player) {
            OnPlayerSpawned?.Invoke(player);
        }

        protected virtual void Awake() {
            // Application-shutdown guard, not a race guard: a game composes this service, so an absent
            // injector means the application is already tearing down.
            if (!Injector.HasInstance) return;

            Injector.Instance.RegisterProvider(this);
        }

        protected virtual void OnDestroy() {
            // Application-quit guard, not a service null-check: with the injector already torn down
            // there is no registry left to clean.
            if (!Injector.HasInstance) return;

            Injector.Instance.UnregisterProvider(this);
        }
    }
}
