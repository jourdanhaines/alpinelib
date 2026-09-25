using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>One built slot on a <see cref="CharacterAppearance"/>: what is worn and what it drew.</summary>
    public sealed class AppearancePiece {
        public AppearancePiece(int slotIndex, AppearanceItem item, ushort itemId, byte variant, GameObject root,
            Renderer[] renderers, Material[][] authoredMaterials, bool hiddenInFirstPerson) {
            SlotIndex = slotIndex;
            Item = item;
            ItemId = itemId;
            Variant = variant;
            Root = root;
            Renderers = renderers;
            AuthoredMaterials = authoredMaterials;
            HiddenInFirstPerson = hiddenInFirstPerson;
        }

        /// <summary>Slot the piece fills.</summary>
        public int SlotIndex { get; }

        /// <summary>Item asset worn.</summary>
        public AppearanceItem Item { get; }

        /// <summary>Catalog id of the item, or 0 when built outside a catalog.</summary>
        public ushort ItemId { get; }

        /// <summary>Material set currently applied.</summary>
        public byte Variant { get; internal set; }

        /// <summary>The piece's root, named <c>Appearance_&lt;SlotKey&gt;</c>.</summary>
        public GameObject Root { get; }

        /// <summary>Every renderer under <see cref="Root"/>.</summary>
        public Renderer[] Renderers { get; }

        /// <summary>Each renderer's prefab materials, restored before a material set is applied.</summary>
        public Material[][] AuthoredMaterials { get; }

        /// <summary>True when the wearer's first-person view draws the piece as shadows only.</summary>
        public bool HiddenInFirstPerson { get; }
    }
}
