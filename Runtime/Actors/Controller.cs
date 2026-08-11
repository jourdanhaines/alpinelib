using UnityEngine;

namespace AlpineLib.Actors {
    /// <summary>
    /// Base class for the brains that drive an <see cref="Actor"/>. A player input reader and an AI
    /// state machine are both controllers, so an actor can be handed from one to the other.
    /// </summary>
    public abstract class Controller : MonoBehaviour {
        /// <summary>
        /// True when this brain places the actor's transform itself — from replicated state, a cutscene
        /// track, anything that already knows where the actor is — rather than steering it through
        /// movement intents.
        /// </summary>
        /// <remarks>
        /// The actor reads this to stand down its own integrators: gravity and air locomotion are
        /// second writers to the same <see cref="CharacterController"/>, and an externally placed pawn
        /// fought by its own physics vibrates. Possession is the seam on purpose — the same actor is
        /// self-simulated under a player brain and externally placed under a replication brain, with
        /// nothing configured anywhere else.
        /// </remarks>
        public virtual bool DrivesPawnExternally => false;

        /// <summary>
        /// Takes control of an actor. Implementations are responsible for calling
        /// <see cref="Actor.Possess"/> on it and for releasing any actor they held before.
        /// </summary>
        public abstract void Possess(Actor character);
    }
}
