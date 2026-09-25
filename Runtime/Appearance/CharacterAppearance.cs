using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Appearance;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// Dresses a character: builds one piece per filled slot onto the model under this object's
    /// <see cref="Animator"/>. The same code previews in the editor and runs in play, where an outfit from
    /// the network, a profile or a tool arrives through <see cref="Apply"/>.
    /// </summary>
    /// <remarks>
    /// Skinned items rebind to the body skeleton by bone name; socket items parent to a named transform.
    /// Pieces are named <c>Appearance_&lt;SlotKey&gt;</c>, lose any collider, rigidbody or animator before
    /// they activate, and are DontSave outside play mode, so a saved prefab or scene stays bare. Prefab
    /// assets and other preview scenes never build.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CharacterAppearance : MonoBehaviour {
        /// <summary>Name prefix of every built piece.</summary>
        public const string PiecePrefix = "Appearance_";

        [Tooltip("Catalog that numbers models and items for the wire.")]
        [SerializeField] private AppearanceCatalog catalog;
        [Tooltip("Character model this body is. Fixed for the life of the character.")]
        [SerializeField] private CharacterModel model;
        [Tooltip("One pick per model slot, in slot order.")]
        [SerializeField] private AppearanceSlotSelection[] slots = Array.Empty<AppearanceSlotSelection>();

        private readonly AppearancePiece[] _pieces = new AppearancePiece[AppearanceOutfit.MaxSlots];
        private readonly List<string> _errors = new List<string>();
        private AppearanceSkeleton _skeleton;
        private IAppearanceVariantResolver _variantResolver = ExactModelVariantResolver.Instance;
        private bool _built;
#if UNITY_EDITOR
        private bool _previewQueued;
#endif

        /// <summary>Raised once after every <see cref="Apply"/>, <see cref="Rebuild"/> or <see cref="Clear"/>.</summary>
        public event Action PiecesChanged;

        /// <summary>Catalog that numbers models and items.</summary>
        public AppearanceCatalog Catalog => catalog;

        /// <summary>Character model this body is.</summary>
        public CharacterModel Model => model;

        /// <summary>True once pieces reflect the slots, until the next <see cref="Clear"/>.</summary>
        public bool IsBuilt => _built;

        /// <summary>Why the last apply or build left something unbuilt.</summary>
        public IReadOnlyList<string> LastErrors => _errors;

        /// <summary>Picks each item's prefab for the model; exact per-model variants by default.</summary>
        public IAppearanceVariantResolver VariantResolver {
            get => _variantResolver;
            set => _variantResolver = value ?? ExactModelVariantResolver.Instance;
        }

        /// <summary>The slots as catalog ids; default when there is no catalog or model.</summary>
        public AppearanceOutfit CurrentOutfit {
            get {
                EnsureSlots();
                if (catalog == null || model == null || model.SlotCount > AppearanceOutfit.MaxSlots) return default;

                var picks = new AppearanceSlotPick[model.SlotCount];
                for (int index = 0; index < picks.Length && index < slots.Length; index++) {
                    picks[index] = PickOf(slots[index]);
                }

                return AppearanceOutfit.Create(catalog.IdOf(model), picks);
            }
        }

        /// <summary>
        /// Adopts an outfit by catalog ids, rebuilding only the slots that changed. A rejected outfit changes
        /// nothing; a slot that fails to build is left empty and the call returns false.
        /// </summary>
        public bool Apply(in AppearanceOutfit outfit) {
            EnsureSlots();
            if (!TryCheckOutfit(outfit, out string error)) {
                Debug.LogError($"CharacterAppearance::Apply->{name} rejected {outfit}: {error}");
                _errors.Clear();
                _errors.Add(error);
                return false;
            }

            if (!_built) ClearPieces();
            _errors.Clear();
            bool allBuilt = true;
            for (int index = 0; index < outfit.SlotCount; index++) {
                allBuilt &= ApplySlot(index, outfit[index]);
            }

            _built = true;
            PiecesChanged?.Invoke();
            return allBuilt;
        }

        /// <summary>Applies a saved profile; false when it does not validate or a slot fails to build.</summary>
        public bool ApplyProfile(AppearanceProfile profile) {
            if (profile == null) {
                Debug.LogError($"CharacterAppearance::ApplyProfile->{name} was given no profile.");
                return false;
            }

            if (profile.TryToOutfit(catalog, out AppearanceOutfit outfit, out string error)) return Apply(outfit);

            Debug.LogError($"CharacterAppearance::ApplyProfile->{name} cannot use profile '{profile.name}': {error}");
            return false;
        }

        /// <summary>Sets one slot's pick without building; for editor tools and tests.</summary>
        public void SetSlot(int slotIndex, AppearanceItem item, int variant) {
            EnsureSlots();
            if (slotIndex < 0 || slotIndex >= slots.Length) {
                throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot {slotIndex} is outside {slots.Length} slot(s).");
            }

            slots[slotIndex] = new AppearanceSlotSelection(item, variant);
        }

        /// <summary>
        /// Clears every piece and builds the slots again. Slots that fail are logged, recorded in
        /// <see cref="LastErrors"/> and left unbuilt; returns true only when all built.
        /// </summary>
        public bool Rebuild() {
            EnsureSlots();
            ClearPieces();
            _skeleton = null;
            _errors.Clear();
            bool allBuilt = BuildSlots();
            _built = true;
            PiecesChanged?.Invoke();
            return allBuilt;
        }

        /// <summary>Destroys every piece, including orphans left by a domain reload.</summary>
        public void Clear() {
            ClearPieces();
            PiecesChanged?.Invoke();
        }

        /// <summary>
        /// Clears <paramref name="into"/> and fills it with the built pieces' renderers — only those hidden
        /// in first person when <paramref name="firstPersonHiddenOnly"/> is set.
        /// </summary>
        public void CollectRenderers(List<Renderer> into, bool firstPersonHiddenOnly) {
            into.Clear();
            foreach (AppearancePiece piece in _pieces) {
                if (piece == null || (firstPersonHiddenOnly && !piece.HiddenInFirstPerson)) continue;

                AddRenderers(piece, into);
            }
        }

        private void Awake() {
            EnsureSlots();
            if (!Application.isPlaying || IsBuilt) return;

            Rebuild();
        }

        private void OnEnable() {
            EnsureSlots();
#if UNITY_EDITOR
            QueuePreview();
#endif
        }

        // Undo/redo, prefab apply/revert, pasted values and script writes all land here; Unity forbids
        // creating or destroying objects inside OnValidate, so the rebuild is deferred.
        private void OnValidate() {
            EnsureSlots();
#if UNITY_EDITOR
            QueuePreview();
#endif
        }

        private void OnDisable() {
#if UNITY_EDITOR
            if (Application.isPlaying) return;

            // Deferred: destroying children while the hierarchy is deactivating is not allowed. The model
            // root is captured so orphans are swept even if this component is removed meanwhile.
            Transform modelRoot = ModelRoot;
            UnityEditor.EditorApplication.delayCall += () => ClearIfStillDisabled(modelRoot);
#endif
        }

#if UNITY_EDITOR
        // Prefab mode previews; other preview scenes (e.g. LoadPrefabContents) and prefab assets do not.
        private bool IsPreviewable() {
            if (Application.isPlaying || !gameObject.scene.IsValid()) return false;
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this)) return false;
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null) return true;

            return !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene);
        }

        // Several OnEnable/OnValidate calls in one frame share a single deferred rebuild.
        private void QueuePreview() {
            if (_previewQueued || !IsPreviewable()) return;

            _previewQueued = true;
            UnityEditor.EditorApplication.delayCall += RebuildPreview;
        }

        private void RebuildPreview() {
            _previewQueued = false;
            if (this == null || !isActiveAndEnabled || !IsPreviewable()) return;

            Rebuild();
        }

        private void ClearIfStillDisabled(Transform modelRoot) {
            if (Application.isPlaying) return;
            if (this == null) {
                SweepOrphans(modelRoot);
                return;
            }

            if (isActiveAndEnabled) return;

            Clear();
        }
#endif

        /// <summary>The transform pieces build under: the model's Animator, or this object.</summary>
        private Transform ModelRoot {
            get {
                Animator animator = GetComponentInChildren<Animator>(true);
                return animator == null ? transform : animator.transform;
            }
        }

        // AddComponent, a model swap and older serialized data leave the array out of step with the slots.
        private void EnsureSlots() {
            if (slots == null) slots = Array.Empty<AppearanceSlotSelection>();
            if (model != null && slots.Length != model.SlotCount) Array.Resize(ref slots, model.SlotCount);

            for (int index = 0; index < slots.Length; index++) {
                if (slots[index] == null) slots[index] = new AppearanceSlotSelection();
            }
        }

        private AppearanceSlotPick PickOf(AppearanceSlotSelection selection) {
            ushort itemId = catalog.IdOf(selection.Item);
            if (itemId == 0) return AppearanceSlotPick.Empty;

            return new AppearanceSlotPick(itemId, (byte)Mathf.Clamp(selection.Variant, 0, byte.MaxValue));
        }

        private bool TryCheckOutfit(in AppearanceOutfit outfit, out string error) {
            error = CheckOwnModel(outfit.ModelId);
            if (error != null) return false;

            return catalog.TryValidate(outfit, _variantResolver, out error);
        }

        private string CheckOwnModel(ushort modelId) {
            if (catalog == null) return "there is no catalog";
            if (model == null) return "there is no character model";
            if (catalog.ModelOf(modelId) != model) return $"the outfit is for model {modelId}, not '{model.name}' (id {catalog.IdOf(model)})";

            return null;
        }

        // Keeps an unchanged piece, swaps materials when only the variant moved, and rebuilds otherwise.
        private bool ApplySlot(int slotIndex, AppearanceSlotPick pick) {
            AppearanceItem item = pick.IsEmpty ? null : catalog.ItemOf(pick.ItemId);
            AppearancePiece piece = _pieces[slotIndex];
            slots[slotIndex] = new AppearanceSlotSelection(item, pick.VariantIndex);
            if (item != null && piece != null && piece.Item == item) {
                if (piece.Variant != pick.VariantIndex) ApplyMaterials(piece, item, pick.VariantIndex);
                return true;
            }

            DestroyPiece(slotIndex);
            if (item == null || TryBuildSlot(slotIndex, item, pick.VariantIndex)) return true;

            slots[slotIndex] = new AppearanceSlotSelection();
            return false;
        }

        private bool BuildSlots() {
            if (model == null) return RecordError("there is no character model");
            if (model.SlotCount > AppearanceOutfit.MaxSlots) return RecordError($"model '{model.name}' has more than {AppearanceOutfit.MaxSlots} slots");

            bool allBuilt = true;
            for (int index = 0; index < slots.Length; index++) {
                AppearanceSlotSelection selection = slots[index];
                if (selection.IsEmpty) continue;

                allBuilt &= TryBuildSelection(index, selection);
            }

            return allBuilt;
        }

        private bool TryBuildSelection(int slotIndex, AppearanceSlotSelection selection) {
            AppearanceItem item = selection.Item;
            string slotKey = model.SlotAt(slotIndex).Key;
            if (item.SlotKey != slotKey) return RecordError($"item '{item.name}' fills slot '{item.SlotKey}', not '{slotKey}'");
            if (selection.Variant < 0 || selection.Variant >= item.VariantCount) {
                return RecordError($"item '{item.name}' has {item.VariantCount} variant(s); variant {selection.Variant} does not exist");
            }

            return TryBuildSlot(slotIndex, item, (byte)selection.Variant);
        }

        /// <summary>
        /// Builds one piece under an inactive staging root so nothing on the item wakes up before it is
        /// stripped and bound, then activates it. On failure nothing is left behind.
        /// </summary>
        private bool TryBuildSlot(int slotIndex, AppearanceItem item, byte variantIndex) {
            AppearanceSlotDefinition slot = model.SlotAt(slotIndex);
            if (!_variantResolver.TryResolve(item, model, out AppearanceItemVariant variant)) return RecordError($"item '{item.name}' has no variant for model '{model.name}'");
            if (variant.Prefab == null) return RecordError($"item '{item.name}' variant for '{model.name}' has no prefab");
            if (!TryGetSkeleton(out AppearanceSkeleton skeleton, out string error)) return RecordError(error);

            GameObject root = CreateStagingRoot(slot.Key);
            bool attached = variant.Attach == AppearanceAttachMode.Skinned
                ? TryAttachSkinned(root, variant, skeleton, out error)
                : TryAttachSocket(root, variant, skeleton, out error);
            if (!attached) {
                AppearancePreview.DestroyAny(root);
                return RecordError($"item '{item.name}' in slot '{slot.Key}': {error}");
            }

            _pieces[slotIndex] = CreatePiece(slotIndex, slot, item, variantIndex, root);
            ApplyMaterials(_pieces[slotIndex], item, variantIndex);
            SetLayer(root, ModelRoot.gameObject.layer);
            if (!Application.isPlaying) AppearancePreview.MarkPreview(root);
            root.SetActive(true);
            return true;
        }

        private GameObject CreateStagingRoot(string slotKey) {
            var root = new GameObject(PiecePrefix + slotKey);
            root.SetActive(false);
            if (!Application.isPlaying) root.hideFlags = AppearancePreview.PreviewFlags;
            root.transform.SetParent(ModelRoot, false);
            return root;
        }

        private AppearancePiece CreatePiece(int slotIndex, AppearanceSlotDefinition slot, AppearanceItem item, byte variantIndex, GameObject root) {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            var authored = new Material[renderers.Length][];
            for (int index = 0; index < renderers.Length; index++) {
                authored[index] = renderers[index].sharedMaterials;
            }

            ushort itemId = catalog == null ? (ushort)0 : catalog.IdOf(item);
            bool hidden = item.IsHiddenInFirstPerson(slot.HiddenInFirstPerson);
            return new AppearancePiece(slotIndex, item, itemId, variantIndex, root, renderers, authored, hidden);
        }

        // The item's own skeleton copy is only a name source: its renderers move onto the staging root
        // bound to the body's bones, and everything else is destroyed.
        private static bool TryAttachSkinned(GameObject root, AppearanceItemVariant variant, AppearanceSkeleton skeleton, out string error) {
            GameObject instance = Instantiate(variant.Prefab, root.transform);
            StripNonVisual(instance);
            SkinnedMeshRenderer[] renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) {
                error = $"skinned prefab '{variant.Prefab.name}' has no SkinnedMeshRenderer";
                return false;
            }

            foreach (SkinnedMeshRenderer renderer in renderers) {
                if (!AppearanceSkinBinder.TryBind(renderer, skeleton, out error)) return false;
            }

            var kept = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer renderer in renderers) {
                kept.Add(renderer.transform);
                renderer.transform.SetParent(root.transform, true);
            }

            DestroyLeftovers(root.transform, kept);
            error = null;
            return true;
        }

        private static bool TryAttachSocket(GameObject root, AppearanceItemVariant variant, AppearanceSkeleton skeleton, out string error) {
            if (!skeleton.TryGet(variant.AttachPoint, out Transform socket)) {
                string problem = skeleton.IsAmbiguous(variant.AttachPoint) ? "is ambiguous" : "is missing";
                error = $"attach point '{variant.AttachPoint}' {problem} on the body";
                return false;
            }

            root.transform.SetParent(socket, false);
            GameObject instance = Instantiate(variant.Prefab, root.transform);
            StripNonVisual(instance);
            error = null;
            return true;
        }

        // Physics on a piece would join the pawn's rigidbody compound; an animator would fight the body's.
        private static void StripNonVisual(GameObject instance) {
            DestroyAllNow(instance.GetComponentsInChildren<Joint>(true));
            DestroyAllNow(instance.GetComponentsInChildren<Collider>(true));
            DestroyAllNow(instance.GetComponentsInChildren<Rigidbody>(true));
            DestroyAllNow(instance.GetComponentsInChildren<Animator>(true));
        }

        private static void DestroyAllNow(Component[] components) {
            foreach (Component component in components) DestroyImmediate(component);
        }

        // Anything hanging off the root or a kept renderer that is not itself a kept renderer.
        private static void DestroyLeftovers(Transform root, HashSet<Transform> kept) {
            var leftovers = new List<Transform>();
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) {
                if (node == root || kept.Contains(node)) continue;
                if (node.parent == root || kept.Contains(node.parent)) leftovers.Add(node);
            }

            foreach (Transform leftover in leftovers) DestroyImmediate(leftover.gameObject);
        }

        private static void ApplyMaterials(AppearancePiece piece, AppearanceItem item, byte variantIndex) {
            AppearanceMaterialSet set = item.MaterialSetAt(variantIndex);
            for (int index = 0; index < piece.Renderers.Length; index++) {
                Renderer renderer = piece.Renderers[index];
                if (renderer == null) continue;

                var materials = (Material[])piece.AuthoredMaterials[index].Clone();
                if (set != null) set.ApplyTo(materials);
                renderer.sharedMaterials = materials;
            }

            piece.Variant = variantIndex;
        }

        private static void SetLayer(GameObject root, int layer) {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
        }

        private static void AddRenderers(AppearancePiece piece, List<Renderer> into) {
            foreach (Renderer renderer in piece.Renderers) {
                if (renderer != null) into.Add(renderer);
            }
        }

        // Built lazily and again whenever the model root changes or dies.
        private bool TryGetSkeleton(out AppearanceSkeleton skeleton, out string error) {
            Transform modelRoot = ModelRoot;
            if (_skeleton != null && _skeleton.IsValid && _skeleton.Root == modelRoot) {
                skeleton = _skeleton;
                error = null;
                return true;
            }

            bool built = AppearanceSkeleton.TryBuild(modelRoot, out _skeleton, out error);
            skeleton = _skeleton;
            return built;
        }

        private bool RecordError(string error) {
            Debug.LogError($"CharacterAppearance::Build->{name} {error}");
            _errors.Add(error);
            return false;
        }

        private void DestroyPiece(int slotIndex) {
            AppearancePiece piece = _pieces[slotIndex];
            _pieces[slotIndex] = null;
            if (piece == null || piece.Root == null) return;

            AppearancePreview.DestroyAny(piece.Root);
        }

        private void ClearPieces() {
            _built = false;
            for (int index = 0; index < _pieces.Length; index++) DestroyPiece(index);

            SweepOrphans(ModelRoot);
        }

        /// <summary>
        /// Destroys every preview piece under <paramref name="modelRoot"/> that nothing tracks any more —
        /// left by a domain reload or a removed component.
        /// </summary>
        private static void SweepOrphans(Transform modelRoot) {
            if (modelRoot == null) return;

            var orphans = new List<GameObject>();
            foreach (Transform node in modelRoot.GetComponentsInChildren<Transform>(true)) {
                if (!node.name.StartsWith(PiecePrefix) || !AppearancePreview.IsPreview(node.gameObject)) continue;
                if (!AppearancePreview.IsLockedByPrefab(node.gameObject)) orphans.Add(node.gameObject);
            }

            foreach (GameObject orphan in orphans) {
                if (orphan != null) AppearancePreview.DestroyAny(orphan);
            }
        }
    }
}
