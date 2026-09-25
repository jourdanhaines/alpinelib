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
    /// and <see cref="TryValidate(in AppearanceOutfit, out string)"/> runs the same rules on the client
    /// over <see cref="ToLenientTable"/>, which skips a malformed row rather than every outfit. The first
    /// live model is the default model. The wire carries one slot index per item, so an item worn by
    /// several models needs its slot key at the same index in each model's slot list.
    /// </remarks>
    [CreateAssetMenu(fileName = "AppearanceCatalog", menuName = "AlpineLib/Appearance/Catalog")]
    public class AppearanceCatalog : ScriptableObject {
        [Tooltip("Append-only. A row's index + 1 is its model id on the wire; never reorder or delete (null out retired rows).")]
        [SerializeField] private List<CharacterModel> models = new List<CharacterModel>();
        [Tooltip("Append-only. A row's index + 1 is its item id on the wire; never reorder or delete (null out retired rows).")]
        [SerializeField] private List<AppearanceItem> items = new List<AppearanceItem>();

        private static readonly Func<CharacterModel, ushort> NoPawnPrefab = (model) => 0;

        private readonly HashSet<string> _warnedProblems = new HashSet<string>();
        private AppearanceCatalogTable _table;
        private IAppearanceVariantResolver _tableResolver;

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
        /// Flattens the catalog into the engine-free table, refusing any malformed row. An item's allowed
        /// models are the catalog models <paramref name="resolver"/> finds a variant for (exact per-model
        /// variants when null); its variant count is its material-set count (at least one).
        /// </summary>
        /// <exception cref="ArgumentException">A model or item row is malformed.</exception>
        public AppearanceCatalogTable ToTable(Func<CharacterModel, ushort> pawnPrefabIdOf, IAppearanceVariantResolver resolver = null) {
            return BuildTable(pawnPrefabIdOf, resolver, true);
        }

        /// <summary>
        /// As <see cref="ToTable"/>, but a malformed model or item row is left out with one warning instead of
        /// failing the whole table, so a half-authored item cannot block every outfit at runtime. Surviving
        /// rows keep their ids.
        /// </summary>
        /// <exception cref="ArgumentException">No usable model is left.</exception>
        public AppearanceCatalogTable ToLenientTable(Func<CharacterModel, ushort> pawnPrefabIdOf, IAppearanceVariantResolver resolver = null) {
            return BuildTable(pawnPrefabIdOf, resolver, false);
        }

        /// <summary>Checks an outfit against the catalog with exact per-model variants.</summary>
        public bool TryValidate(in AppearanceOutfit outfit, out string error) {
            return TryValidate(outfit, ExactModelVariantResolver.Instance, out error);
        }

        /// <summary>
        /// Checks an outfit against the lenient table built with <paramref name="resolver"/>, then that the
        /// resolver finds a prefab for every worn item on the outfit's model.
        /// </summary>
        public bool TryValidate(in AppearanceOutfit outfit, IAppearanceVariantResolver resolver, out string error) {
            resolver = resolver ?? ExactModelVariantResolver.Instance;
            if (!TryGetTable(resolver, out AppearanceCatalogTable table, out error)) return false;
            if (!AppearanceRules.Validate(outfit, table, out error)) return false;

            error = CheckResolvable(outfit, resolver);
            return error == null;
        }

        /// <summary>Drops the cached table; call after editing model or item assets at runtime.</summary>
        public void InvalidateTable() {
            _table = null;
            _tableResolver = null;
            _warnedProblems.Clear();
        }

        private void OnValidate() {
            InvalidateTable();
        }

        // Edit mode never trusts the cache: item and model assets change without this asset's OnValidate.
        private bool TryGetTable(IAppearanceVariantResolver resolver, out AppearanceCatalogTable table, out string error) {
            error = null;
            if (_table != null && Application.isPlaying && ReferenceEquals(_tableResolver, resolver)) {
                table = _table;
                return true;
            }

            try {
                _table = ToLenientTable(NoPawnPrefab, resolver);
                _tableResolver = resolver;
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

        private AppearanceCatalogTable BuildTable(Func<CharacterModel, ushort> pawnPrefabIdOf, IAppearanceVariantResolver resolver, bool strict) {
            if (pawnPrefabIdOf == null) throw new ArgumentNullException(nameof(pawnPrefabIdOf));

            resolver = resolver ?? ExactModelVariantResolver.Instance;
            var tableModels = new List<CharacterModel>();
            List<AppearanceModelInfo> modelInfos = BuildModelInfos(pawnPrefabIdOf, strict, tableModels);
            List<AppearanceItemInfo> itemInfos = BuildItemInfos(resolver, strict, tableModels);
            ushort defaultModelId = tableModels.Count == 0 ? (ushort)0 : IdOf(tableModels[0]);
            return new AppearanceCatalogTable(defaultModelId, modelInfos, itemInfos);
        }

        private List<AppearanceModelInfo> BuildModelInfos(Func<CharacterModel, ushort> pawnPrefabIdOf, bool strict, List<CharacterModel> tableModels) {
            var infos = new List<AppearanceModelInfo>();
            for (int index = 0; index < models.Count; index++) {
                CharacterModel model = models[index];
                if (model == null) continue;

                string problem = DescribeModelProblem(model);
                if (problem != null) {
                    Reject(model, problem, strict);
                    continue;
                }

                tableModels.Add(model);
                infos.Add(new AppearanceModelInfo((ushort)(index + 1), (byte)model.SlotCount, pawnPrefabIdOf(model)));
            }

            return infos;
        }

        private static string DescribeModelProblem(CharacterModel model) {
            if (model.SlotCount > 0 && model.SlotCount <= AppearanceOutfit.MaxSlots) return null;

            return $"Model '{model.name}' has {model.SlotCount} slots; 1 to {AppearanceOutfit.MaxSlots} are allowed.";
        }

        private List<AppearanceItemInfo> BuildItemInfos(IAppearanceVariantResolver resolver, bool strict, List<CharacterModel> tableModels) {
            var infos = new List<AppearanceItemInfo>();
            for (int index = 0; index < items.Count; index++) {
                AppearanceItem item = items[index];
                if (item == null) continue;

                if (TryBuildItemInfo(item, (ushort)(index + 1), resolver, tableModels, out AppearanceItemInfo info, out string problem)) {
                    infos.Add(info);
                    continue;
                }

                Reject(item, problem, strict);
            }

            return infos;
        }

        private bool TryBuildItemInfo(AppearanceItem item, ushort itemId, IAppearanceVariantResolver resolver,
            List<CharacterModel> tableModels, out AppearanceItemInfo info, out string problem) {
            info = default;
            problem = DescribeVariantProblem(item);
            if (problem != null) return false;

            var allowedModelIds = new List<ushort>();
            int slotIndex = -1;
            foreach (CharacterModel model in tableModels) {
                if (!resolver.TryResolve(item, model, out _)) continue;

                int modelSlot = model.IndexOfSlot(item.SlotKey);
                problem = DescribeSlotProblem(item, model, modelSlot, slotIndex);
                if (problem != null) return false;

                slotIndex = modelSlot;
                allowedModelIds.Add(IdOf(model));
            }

            if (slotIndex < 0) {
                problem = $"Item '{item.name}' has no variant any model in the catalog can wear.";
                return false;
            }

            info = new AppearanceItemInfo(itemId, (byte)slotIndex, (byte)item.VariantCount, allowedModelIds);
            return true;
        }

        private string DescribeVariantProblem(AppearanceItem item) {
            if (item.VariantCount > byte.MaxValue) return $"Item '{item.name}' has {item.VariantCount} material sets; at most {byte.MaxValue} are allowed.";

            foreach (AppearanceItemVariant variant in item.Variants) {
                if (variant == null) continue;
                if (variant.Model == null) return $"Item '{item.name}' has a variant with no model.";
                if (IdOf(variant.Model) == 0) return $"Item '{item.name}' has a variant for model '{variant.Model.name}', which is not in the catalog.";
            }

            return null;
        }

        // The wire carries one slot index per item, so every model wearing it needs the key at that index.
        private static string DescribeSlotProblem(AppearanceItem item, CharacterModel model, int modelSlot, int slotIndex) {
            if (modelSlot < 0) return $"Item '{item.name}' fills slot '{item.SlotKey}', which model '{model.name}' does not have.";
            if (slotIndex >= 0 && modelSlot != slotIndex) {
                return $"Item '{item.name}' slot '{item.SlotKey}' is slot {slotIndex} on one model but {modelSlot} on '{model.name}'; a slot key must keep one index across models.";
            }

            return null;
        }

        // Strict tables refuse the catalog; lenient ones skip the row and warn once per problem.
        private void Reject(UnityEngine.Object row, string problem, bool strict) {
            if (strict) throw new ArgumentException(problem);
            if (!_warnedProblems.Add(problem)) return;

            Debug.LogWarning($"AppearanceCatalog::ToLenientTable->'{name}' skips '{row.name}': {problem}", row);
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
