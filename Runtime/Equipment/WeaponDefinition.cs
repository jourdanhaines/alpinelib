using AlpineLib.Skills;
using AlpineLib.Stats;
using AlpineLib.Tags;
using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Everything a weapon contributes to the actor holding it: the damage its attacks are built on,
    /// the locomotion it re-animates, the model attached to the hand, the skills it grants while held,
    /// and the implicit stat modifiers it applies.
    /// </summary>
    /// <remarks>
    /// A weapon is pure data — nothing here is instanced. <see cref="EquipmentSystem"/> owns the
    /// lifetime of everything the weapon produces (visual instance, granted skills, modifiers) and
    /// reverses it on unequip, so the same asset can be equipped by any number of actors at once.
    ///
    /// <see cref="tags"/> is the weapon's contribution to damage-query context: modifiers tagged
    /// "Sword" only apply to skills used with a sword because the skill's query context is the union
    /// of the skill's own tags and the equipped weapon's. Leaving it empty makes the weapon
    /// untaggable — no tagged modifier will ever recognise it.
    /// </remarks>
    [CreateAssetMenu(fileName = "WeaponDefinition", menuName = "AlpineLib/Equipment/Weapon Definition")]
    public class WeaponDefinition : ScriptableObject {
        [Tooltip("Name shown in inventory and tooltips")]
        public string displayName;

        [Tooltip("Icon shown in inventory and tooltips")]
        public Sprite icon;

        [Tooltip("Tags folded into the damage query context while this weapon is held")]
        public TagSet tags;

        [Tooltip("Weapon damage added to skills that opt into it")]
        public float baseDamage;

        [Tooltip("Locomotion animations swapped in while this weapon is held")]
        public AnimatorOverrideController locomotionOverride;

        [Tooltip("Model instantiated on the attach bone while this weapon is held")]
        public GameObject visualPrefab;

        [Tooltip("Bone the visual prefab is parented to, searched by name under the animator")]
        public string attachBoneName = "mixamorig:RightHand";

        [Tooltip("Skills usable only while this weapon is held")]
        public SkillDefinition[] grantedSkills;

        [Tooltip("Stat modifiers applied for as long as this weapon is held")]
        public StatModifier[] implicitModifiers;
    }
}
