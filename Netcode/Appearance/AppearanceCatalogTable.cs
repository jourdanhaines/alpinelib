using System;
using System.Collections.Generic;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// An immutable, engine-free appearance catalog: the table a Unity catalog asset flattens to and the
    /// dedicated server loads from JSON, so both ends validate outfits against the same rows.
    /// </summary>
    /// <remarks>
    /// The constructor refuses any table a valid outfit could not be checked against — zero or duplicate
    /// ids, models with no slots or more than <see cref="AppearanceOutfit.MaxSlots"/>, items with no
    /// variants or a slot no allowed model has, allowed models or a default model the table lacks.
    /// </remarks>
    public sealed class AppearanceCatalogTable : IAppearanceCatalog {
        private readonly Dictionary<ushort, AppearanceModelInfo> modelsById = new Dictionary<ushort, AppearanceModelInfo>();
        private readonly Dictionary<ushort, AppearanceItemInfo> itemsById = new Dictionary<ushort, AppearanceItemInfo>();

        public AppearanceCatalogTable(ushort defaultModelId, IEnumerable<AppearanceModelInfo> models, IEnumerable<AppearanceItemInfo> items) {
            if (models == null) {
                throw new ArgumentNullException(nameof(models));
            }

            if (items == null) {
                throw new ArgumentNullException(nameof(items));
            }

            foreach (AppearanceModelInfo model in models) {
                AddModel(model);
            }

            foreach (AppearanceItemInfo item in items) {
                AddItem(item);
            }

            if (!modelsById.ContainsKey(defaultModelId)) {
                throw new ArgumentException($"Default model {defaultModelId} is not in the catalog.", nameof(defaultModelId));
            }

            DefaultModelId = defaultModelId;
            Models = SortById(modelsById, (model) => model.Id);
            Items = SortById(itemsById, (item) => item.Id);
        }

        /// <inheritdoc />
        public ushort DefaultModelId { get; }

        /// <summary>Every model, ascending by id.</summary>
        public IReadOnlyList<AppearanceModelInfo> Models { get; }

        /// <summary>Every item, ascending by id.</summary>
        public IReadOnlyList<AppearanceItemInfo> Items { get; }

        /// <inheritdoc />
        public bool TryGetModel(ushort modelId, out AppearanceModelInfo model) {
            return modelsById.TryGetValue(modelId, out model);
        }

        /// <inheritdoc />
        public bool TryGetItem(ushort itemId, out AppearanceItemInfo item) {
            return itemsById.TryGetValue(itemId, out item);
        }

        private void AddModel(AppearanceModelInfo model) {
            if (model.Id == 0) {
                throw new ArgumentException("Model id 0 is reserved.", "models");
            }

            if (model.SlotCount == 0 || model.SlotCount > AppearanceOutfit.MaxSlots) {
                throw new ArgumentException($"Model {model.Id} has {model.SlotCount} slots; 1 to {AppearanceOutfit.MaxSlots} are allowed.", "models");
            }

            if (modelsById.ContainsKey(model.Id)) {
                throw new ArgumentException($"Model id {model.Id} appears twice.", "models");
            }

            modelsById.Add(model.Id, model);
        }

        private void AddItem(AppearanceItemInfo item) {
            if (item == null) {
                throw new ArgumentException("Items may not contain null.", "items");
            }

            if (item.Id == 0) {
                throw new ArgumentException("Item id 0 means an empty slot and cannot name an item.", "items");
            }

            if (item.VariantCount == 0) {
                throw new ArgumentException($"Item {item.Id} has no variants.", "items");
            }

            if (item.SlotIndex >= AppearanceOutfit.MaxSlots) {
                throw new ArgumentException($"Item {item.Id} fills slot {item.SlotIndex}, beyond the {AppearanceOutfit.MaxSlots}-slot cap.", "items");
            }

            if (itemsById.ContainsKey(item.Id)) {
                throw new ArgumentException($"Item id {item.Id} appears twice.", "items");
            }

            CheckAllowedModels(item);
            itemsById.Add(item.Id, item);
        }

        private void CheckAllowedModels(AppearanceItemInfo item) {
            foreach (ushort modelId in item.AllowedModelIds) {
                if (!modelsById.TryGetValue(modelId, out AppearanceModelInfo model)) {
                    throw new ArgumentException($"Item {item.Id} allows model {modelId}, which is not in the catalog.", "items");
                }

                if (item.SlotIndex >= model.SlotCount) {
                    throw new ArgumentException($"Item {item.Id} fills slot {item.SlotIndex}, but model {modelId} has {model.SlotCount} slots.", "items");
                }
            }
        }

        private static IReadOnlyList<TValue> SortById<TValue>(Dictionary<ushort, TValue> byId, Func<TValue, ushort> idOf) {
            var sorted = new List<TValue>(byId.Values);
            sorted.Sort((left, right) => idOf(left).CompareTo(idOf(right)));
            return sorted.AsReadOnly();
        }
    }
}
