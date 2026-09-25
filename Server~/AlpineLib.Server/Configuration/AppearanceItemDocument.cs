using System.Collections.Generic;
using AlpineLib.Netcode.Appearance;

namespace AlpineLib.Server.Configuration {
    /// <summary>JSON mirror of one exported appearance item row.</summary>
    public sealed class AppearanceItemDocument {
        /// <summary>Wire id of the item. Never 0.</summary>
        public ushort Id { get; set; }

        /// <summary>The slot the item fills.</summary>
        public byte SlotIndex { get; set; }

        /// <summary>How many material variants the item has.</summary>
        public byte VariantCount { get; set; }

        /// <summary>Models the item fits, or empty for any model.</summary>
        public List<ushort> AllowedModelIds { get; set; } = new List<ushort>();

        /// <summary>Maps this row onto the engine-free item info.</summary>
        public AppearanceItemInfo ToInfo() {
            return new AppearanceItemInfo(Id, SlotIndex, VariantCount, AllowedModelIds);
        }
    }
}
