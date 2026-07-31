using UnityEngine;

namespace AlpineLib.Actors {
    /// <summary>
    /// Base class for the brains that drive an <see cref="Actor"/>. A player input reader and an AI
    /// state machine are both controllers, so an actor can be handed from one to the other.
    /// </summary>
    public abstract class Controller : MonoBehaviour {
        /// <summary>
        /// Takes control of an actor. Implementations are responsible for calling
        /// <see cref="Actor.Possess"/> on it and for releasing any actor they held before.
        /// </summary>
        public abstract void Possess(Actor character);
    }
}
