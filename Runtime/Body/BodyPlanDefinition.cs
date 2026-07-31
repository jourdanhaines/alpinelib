using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Body {
    /// <summary>
    /// The anatomy of one kind of creature: every body part a <see cref="BodySystem"/> using this
    /// plan will track. A humanoid plan ships with the library; games add their own for anything
    /// shaped differently.
    /// </summary>
    [CreateAssetMenu(fileName = "BodyPlanDefinition", menuName = "AlpineLib/Body/Body Plan Definition")]
    public class BodyPlanDefinition : ScriptableObject {
        /// <summary>
        /// Parts this plan is made of. Order is authoring order and is preserved when a body system
        /// builds its parts, so tooling lists them head to toe if the asset does.
        /// </summary>
        public List<BodyPartDefinition> parts = new();
    }
}
