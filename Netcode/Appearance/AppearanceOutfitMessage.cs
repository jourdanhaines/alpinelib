using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// One player's outfit on the wire. Sent server to client as the authoritative record, and client to
    /// server as a request to change it.
    /// </summary>
    /// <remarks>
    /// Layout: model id (ushort), slot count (byte), then per slot item id (ushort) and variant (byte).
    /// A count above <see cref="AppearanceOutfit.MaxSlots"/> is rejected before any slot is read, so a
    /// hostile packet cannot make the reader allocate beyond the cap.
    /// </remarks>
    public struct AppearanceOutfitMessage : INetMessage {
        /// <summary>Bytes before the first slot: model id and slot count.</summary>
        public const int HeaderByteCount = 3;

        /// <summary>Bytes each slot occupies: item id and variant.</summary>
        public const int SlotByteCount = 3;

        /// <summary>Largest message possible, at <see cref="AppearanceOutfit.MaxSlots"/> slots.</summary>
        public const int MaxByteCount = HeaderByteCount + SlotByteCount * AppearanceOutfit.MaxSlots;

        public AppearanceOutfitMessage(AppearanceOutfit outfit) {
            Outfit = outfit;
        }

        /// <summary>The outfit this message carries.</summary>
        public AppearanceOutfit Outfit { get; set; }

        /// <summary>Bytes a message with <paramref name="slotCount"/> slots occupies on the wire.</summary>
        public static int ByteCountFor(int slotCount) {
            return HeaderByteCount + SlotByteCount * slotCount;
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            AppearanceOutfit outfit = Outfit;
            writer.WriteUShort(outfit.ModelId);
            writer.WriteByte((byte)outfit.SlotCount);
            for (int index = 0; index < outfit.SlotCount; index++) {
                AppearanceSlotPick pick = outfit[index];
                writer.WriteUShort(pick.ItemId);
                writer.WriteByte(pick.VariantIndex);
            }
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            ushort modelId = reader.ReadUShort();
            int slotCount = reader.ReadByte();
            if (slotCount > AppearanceOutfit.MaxSlots) {
                throw new NetProtocolException($"Appearance outfit declares {slotCount} slots; at most {AppearanceOutfit.MaxSlots} are allowed.");
            }

            var picks = new AppearanceSlotPick[slotCount];
            for (int index = 0; index < slotCount; index++) {
                ushort itemId = reader.ReadUShort();
                picks[index] = new AppearanceSlotPick(itemId, reader.ReadByte());
            }

            Outfit = AppearanceOutfit.Create(modelId, picks);
        }
    }
}
