using AlpineLib.Netcode.Appearance;

namespace AlpineLib.Server.Configuration {
    /// <summary>JSON mirror of one exported character model row.</summary>
    public sealed class AppearanceModelDocument {
        /// <summary>Wire id of the model. Never 0.</summary>
        public ushort Id { get; set; }

        /// <summary>How many outfit slots the model has; the length of every outfit for it.</summary>
        public byte SlotCount { get; set; }

        /// <summary>The pawn prefab a member choosing this model is spawned as.</summary>
        public ushort PawnPrefabId { get; set; }

        /// <summary>Maps this row onto the engine-free model info.</summary>
        public AppearanceModelInfo ToInfo() {
            return new AppearanceModelInfo(Id, SlotCount, PawnPrefabId);
        }
    }
}
