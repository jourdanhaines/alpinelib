using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Appearance;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// Every character model and wearable item a game ships, numbered for the wire: a row's id is its
    /// index + 1, so 0 always means none.
    /// </summary>
    /// <remarks>
    /// Both lists are append-only; a retired row stays in place as null. <see cref="ToTable"/> flattens
    /// the assets into the engine-free <see cref="AppearanceCatalogTable"/> the server validates against,
    /// and <see cref="TryValidate(in AppearanceOutfit, out string)"/> runs the same rules on the client.
    /// The first live model is the default model.
    /// </remarks>
    [CreateAssetMenu(fileName = "AppearanceCatalog", menuName = "AlpineLib/Appearance/Catalog")]
    public class AppearanceCatalog : ScriptableObject {
        [Tooltip("Append-only. A row's index + 1 is its model id on the wire; never reorder or delete (null out retired rows).")]
        [SerializeField] private List<CharacterModel> models = new List<CharacterModel>();
        [Tooltip("Append-only. A row's index + 1 is its item id on the wire; never reorder or delete (null out retired rows).")]
        [SerializeField] private List<AppearanceItem> items = new List<AppearanceItem>();

        private static readonly Func<CharacterModel, ushort> NoPawnPrefab = (model) => 0;

        private AppearanceCatalogTable _table;

        /// <summary>Model rows; index + 1 is the id.</summary>
        public IReadOnlyList<CharacterModel> Models => models;

        /// <summary>Item rows; index + 1 is the id.</summary>
        public IReadOnlyList<AppearanceItem> Items => items;

        /// <summary>Creates an in-memory catalog; for tools and tests.</summary>
        public static AppearanceCatalog Create(IEnumerable<CharacterModel> models, IEnumerable<AppearanceItem> items) {
            AppearanceCatalog catalog = CreateInstance<AppearanceCatalog>();
            catalog.models = new List<CharacterModel>(models);
            catalog.items = new List<AppearanceItem>(items);
            return catalog;
        }

        /// <summary>Wire id of <paramref name="model"/>, or 0 when it is not in the catalog.</summary>
        public ushort IdOf(CharacterModel model) {
            return IdOfRow(models, model);
        }

        /// <summary>The model with wire id <paramref name="modelId"/>, or null.</summary>
        public CharacterModel ModelOf(ushort modelId) {
            return RowOf(models, modelId);
        }

        /// <summary>Wire id of <paramref name="item"/>, or 0 when it is not in the catalog.</summary>
        public ushort IdOf(AppearanceItem item) {
            return IdOfRow(items, item);
        }

        /// <summary>The item with wire id <paramref name="itemId"/>, or null.</summary>
        public AppearanceItem ItemOf(ushort itemId) {
            return RowOf(items, itemId);
        }

        /// <summary>Fills <paramref name="into"/> with every item <paramref name="model"/> can wear in a slot.</summary>
        public void CollectItemsFor(CharacterModel model, int slotIndex, List<AppearanceItem> into) {
            into.Clear();
            AppearanceSlotDefinition slot = model == null ? null : model.SlotAt(slotIndex);
            if (slot == null || items == null) return;

            foreach (AppearanceItem item in items) {
                if (item == null || item.SlotKey != slot.Key) continue;
                if (item.TryGetVariantFor(model, out _)) into.Add(item);
            }
        }

        /// <summary>
        /// Flattens the catalog into the engine-free table. An item's allowed models are the models its
        /// variants name; its variant count is its material-set count (at least one).
        /// </summary>
        /// <exception cref="ArgumentException">The assets describe a catalog no outfit could be checked against.</exception>
        public AppearanceCatalogTable ToTable(Func<CharacterModel, ushort> pawnPrefabIdOf) {
            if (pawnPrefabIdOf == null) throw new ArgumentNullException(nameof(pawnPrefabIdOf));

            List<AppearanceModelInfo> modelInfos = BuildModelInfos(pawnPrefabIdOf, out ushort defaultModelId);
            List<AppearanceItemInfo> itemInfos = BuildItemInfos();
            return new AppearanceCatalogTable(defaultModelId, modelInfos, itemInfos);
        }

        /// <summary>Checks an outfit against the catalog with exact per-model variants.</summary>
        public bool TryValidate(in AppearanceOutfit outfit, out string error) {
            return TryValidate(outfit, ExactModelVariantResolver.Instance, out error);
        }

        /// <summary>
        /// Checks an outfit against the catalog rules, then that <paramref name="resolver"/> finds a prefab
        /// for every worn item on the outfit's model.
        /// </summary>
        public bool TryValidate(in AppearanceOutfit outfit, IAppearanceVariantResolver resolver, out string error) {
            if (!TryGetTable(out AppearanceCatalogTable table, out error)) return false;
            if (!AppearanceRules.Validate(outfit, table, out error)) return false;

            error = CheckResolvable(outfit, resolver ?? ExactModelVariantResolver.Instance);
            return error == null;
        }

        /// <summary>Drops the cached table; call after editing model or item assets at runtime.</summary>
        public void InvalidateTable() {
            _table = null;
        }

        private void OnValidate() {
            InvalidateTable();
        }

        // Edit mode never trusts the cache: item and model assets change without this asset's OnValidate.
        private bool TryGetTable(out AppearanceCatalogTable table, out string error) {
            error = null;
            if (_table != null && Application.isPlaying) {
                table = _table;
                return true;
            }

            try {
                _table = ToTable(NoPawnPrefab);
            } catch (ArgumentException exception) {
                _table = null;
                error = $"Catalog '{name}' is invalid: {exception.Message}";
            }

            table = _table;
            return table != null;
        }

        private string CheckResolvable(in AppearanceOutfit outfit, IAppearanceVariantResolver resolver) {
            CharacterModel model = ModelOf(outfit.ModelId);
            for (int index = 0; index < outfit.SlotCount; index++) {
                AppearanceSlotPick pick = outfit[index];
                if (pick.IsEmpty) continue;

                AppearanceItem item = ItemOf(pick.ItemId);
                if (!resolver.TryResolve(item, model, out _)) return $"Item '{item.name}' has no variant for model '{model.Key}'.";
            }

            return null;
        }

        private List<AppearanceModelInfo> BuildModelInfos(Func<CharacterModel, ushort> pawnPrefabIdOf, out ushort defaultModelId) {
            defaultModelId = 0;
            var infos = new List<AppearanceModelInfo>();
            for (int index = 0; index < models.Count; index++) {
                CharacterModel model = models[index];
                if (model == null) continue;
                if (model.SlotCount > AppearanceOutfit.MaxSlots) {
                    throw new ArgumentException($"Model '{model.name}' has {model.SlotCount} slots; at most {AppearanceOutfit.MaxSlots} are allowed.");
                }

                ushort modelId = (ushort)(index + 1);
                if (defaultModelId == 0) defaultModelId = modelId;
                infos.Add(new AppearanceModelInfo(modelId, (byte)model.SlotCount, pawnPrefabIdOf(model)));
            }

            return infos;
        }

        private List<AppearanceItemInfo> BuildItemInfos() {
            var infos = new List<AppearanceItemInfo>();
            for (int index = 0; index < items.Count; index++) {
                if (items[index] == null) continue;

                infos.Add(BuildItemInfo(items[index], (ushort)(index + 1)));
            }

            return infos;
        }

        private AppearanceItemInfo BuildItemInfo(AppearanceItem item, ushort itemId) {
            if (item.VariantCount > byte.MaxValue) {
                throw new ArgumentException($"Item '{item.name}' has {item.VariantCount} material sets; at most {byte.MaxValue} are allowed.");
            }

            var allowedModelIds = new List<ushort>();
            int slotIndex = -1;
            foreach (AppearanceItemVariant variant in item.Variants) {
                if (variant == null) continue;

                int variantSlot = SlotIndexFor(item, variant.Model);
                if (slotIndex >= 0 && variantSlot != slotIndex) {
                    throw new ArgumentException($"Item '{item.name}' slot '{item.SlotKey}' is slot {slotIndex} on one model but {variantSlot} on '{variant.Model.name}'; a slot key must keep one index across models.");
                }

                slotIndex = variantSlot;
                allowedModelIds.Add(IdOf(variant.Model));
            }

            if (slotIndex < 0) throw new ArgumentException($"Item '{item.name}' has no variants.");

            return new AppearanceItemInfo(itemId, (byte)slotIndex, (byte)item.VariantCount, allowedModelIds);
        }

        private int SlotIndexFor(AppearanceItem item, CharacterModel model) {
            if (model == null) throw new ArgumentException($"Item '{item.name}' has a variant with no model.");
            if (IdOf(model) == 0) throw new ArgumentException($"Item '{item.name}' has a variant for model '{model.name}', which is not in the catalog.");

            int slotIndex = model.IndexOfSlot(item.SlotKey);
            if (slotIndex < 0) throw new ArgumentException($"Item '{item.name}' fills slot '{item.SlotKey}', which model '{model.name}' does not have.");

            return slotIndex;
        }

        private static ushort IdOfRow<TRow>(List<TRow> rows, TRow row) where TRow : UnityEngine.Object {
            if (row == null || rows == null) return 0;

            int index = rows.IndexOf(row);
            return index < 0 ? (ushort)0 : (ushort)(index + 1);
        }

        private static TRow RowOf<TRow>(List<TRow> rows, ushort id) where TRow : UnityEngine.Object {
            if (id == 0 || rows == null || id > rows.Count) return null;

            return rows[id - 1];
        }
    }
}
