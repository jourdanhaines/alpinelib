using System;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>An item's prefab for one character model, and how that prefab attaches.</summary>
    [Serializable]
    public class AppearanceItemVariant {
        [Tooltip("Character model this prefab is made for.")]
        [SerializeField] private CharacterModel model;
        [Tooltip("Prefab instantiated on the character. Colliders, rigidbodies and animators are stripped.")]
        [SerializeField] private GameObject prefab;
        [Tooltip("Skinned: meshes rebound to the body skeleton by bone name. Socket: rigid, parented to the attach point.")]
        [SerializeField] private AppearanceAttachMode attach;
        [Tooltip("Socket mode only: name of the transform under the model to parent to (e.g. Socket_Hat).")]
        [SerializeField] private string attachPoint;

        public AppearanceItemVariant() {
        }

        public AppearanceItemVariant(CharacterModel model, GameObject prefab, AppearanceAttachMode attach, string attachPoint) {
            this.model = model;
            this.prefab = prefab;
            this.attach = attach;
            this.attachPoint = attachPoint;
        }

        /// <summary>Character model this prefab is made for.</summary>
        public CharacterModel Model => model;

        /// <summary>Prefab instantiated on the character.</summary>
        public GameObject Prefab => prefab;

        /// <summary>How the prefab joins the character.</summary>
        public AppearanceAttachMode Attach => attach;

        /// <summary>Socket transform name for <see cref="AppearanceAttachMode.Socket"/>.</summary>
        public string AttachPoint => attachPoint;
    }
}
