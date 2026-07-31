using System.Collections.Generic;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Body {
    /// <summary>
    /// Template for one kind of wound. Attacks and hazards reference an injury definition and spawn
    /// <see cref="Injury"/> instances from it at a chosen severity.
    /// </summary>
    [CreateAssetMenu(fileName = "InjuryDefinition", menuName = "AlpineLib/Body/Injury Definition")]
    public class InjuryDefinition : ScriptableObject {
        [Tooltip("Multiplied by the injury's severity and by the body part's severity multiplier")]
        public float baseBleedRate = 0.1f;

        [Tooltip("Timed conditions this wound can develop, each rolled once when the injury is applied")]
        public List<InjuryCondition> conditions = new();

        [Tooltip("Stat debuffs this injury applies while it is present")]
        public StatModifier[] statModifiers;
    }
}
