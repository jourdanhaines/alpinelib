namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// Lookup of character models and wearable items by catalog id — what <see cref="AppearanceRules"/>
    /// validates an outfit against.
    /// </summary>
    public interface IAppearanceCatalog {
        /// <summary>Model a player gets when they ask for none, or for one the catalog does not know.</summary>
        ushort DefaultModelId { get; }

        /// <summary>Finds the model with <paramref name="modelId"/>.</summary>
        bool TryGetModel(ushort modelId, out AppearanceModelInfo model);

        /// <summary>Finds the item with <paramref name="itemId"/>.</summary>
        bool TryGetItem(ushort itemId, out AppearanceItemInfo item);
    }
}
