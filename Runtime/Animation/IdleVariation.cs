using System;
using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>
    /// One entry in an <see cref="IdleVariationSystem"/> set: the base-layer trigger that starts the
    /// variation, and an optional expression trigger fired alongside it.
    /// </summary>
    /// <remarks>
    /// A plain serialized pair rather than a ScriptableObject because a variation has no identity
    /// beyond the actor it is authored on — the clip, the state and the trigger all live in that
    /// actor's animator controller, so there is nothing for a shared asset to share.
    /// </remarks>
    [Serializable]
    public class IdleVariation {
        [Tooltip("Trigger on the base layer that starts the variation state")]
        public string variationTrigger;

        [Tooltip("Expression-layer trigger fired together with the variation; empty for none")]
        public string expressionTrigger;
    }
}
