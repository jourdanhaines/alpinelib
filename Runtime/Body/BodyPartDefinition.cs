using UnityEngine;

namespace AlpineLib.Body {
    /// <summary>
    /// Asset identifying one anatomical location. Games author one asset per body part and list them
    /// on a <see cref="BodyPlanDefinition"/>, so anatomy lives in data instead of in an enum.
    /// </summary>
    [CreateAssetMenu(fileName = "BodyPartDefinition", menuName = "AlpineLib/Body/Body Part Definition")]
    public class BodyPartDefinition : ScriptableObject {
        /// <summary>
        /// Human readable name for user interfaces and tooling.
        /// </summary>
        public string displayName;

        /// <summary>
        /// Scales everything routed through this part: incoming damage and the bleed rate of the
        /// injuries it carries. Values above one make the part more vulnerable than average.
        /// </summary>
        public float severityMultiplier = 1f;
    }
}
