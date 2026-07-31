using AlpineLib.Body;
using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// Implemented by components that want to react to their owner being hit — flinch animations,
    /// pain noises, threat reactions. The attacker looks the receiver up from the hurt box that was
    /// struck, so reactions stay out of the actor and combat types entirely.
    /// </summary>
    public interface IHitReceiver {
        /// <summary>
        /// Called once per landed hit, after the injury has been applied.
        /// </summary>
        /// <param name="attacker">Game object that dealt the hit.</param>
        /// <param name="bodyPart">Part that was struck.</param>
        void NotifyHit(GameObject attacker, BodyPartDefinition bodyPart);
    }
}
