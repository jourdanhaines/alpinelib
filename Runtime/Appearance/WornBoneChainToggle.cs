using System;
using System.Collections.Generic;
using AlpineLib.Animation.Procedural;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// Switches a character's <see cref="BoneChainDynamics"/> on only while something worn hangs on its chain:
    /// whenever the <see cref="CharacterAppearance"/> rebuilds, each chain's weight becomes 1 when a built
    /// skinned piece is bound to the chain's trigger bone and 0 otherwise, so a long nose or a tail swings and
    /// a short one leaves the bones at rest.
    /// </summary>
    /// <remarks>
    /// The trigger is a bone name (by default the chain's first link): a piece counts when some vertex carries
    /// weight on it, so any item skinned to it joins and the rest are ignored. Skinned exports often list every
    /// bone of the rig, so the weights decide: an item meant to set a chain swinging must import readable, and an
    /// unreadable piece never counts. Chains default to those on this object or its children.
    /// Runs in edit mode too, so previews and gates that pose a body see the weight.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WornBoneChainToggle : MonoBehaviour {
        [Tooltip("Appearance whose built pieces switch the chains; this object's when empty.")]
        [SerializeField] private CharacterAppearance appearance;

        [Tooltip("Chains switched; every chain on this object and its children when empty.")]
        [SerializeField] private BoneChainDynamics[] chains = new BoneChainDynamics[0];

        [Tooltip("Bone a worn skinned piece must be bound to for the chains to run; each chain's first link when empty.")]
        [SerializeField] private string triggerBone;

        private readonly List<Renderer> _pieceRenderers = new List<Renderer>();
        private CharacterAppearance _subscribed;

        /// <summary>Bone a worn skinned piece must be bound to; empty means each chain's first link.</summary>
        public string TriggerBone => triggerBone;

        /// <summary>Sets the appearance, chains and trigger bone, e.g. from an assembler; null or empty picks the defaults.</summary>
        public void Configure(CharacterAppearance source, BoneChainDynamics[] targetChains, string trigger) {
            Unsubscribe();
            appearance = source;
            chains = targetChains ?? new BoneChainDynamics[0];
            triggerBone = trigger;
            if (isActiveAndEnabled) Subscribe();
        }

        /// <summary>Sets every chain's weight from the built pieces. Public so gates can force it.</summary>
        public void Refresh() {
            CharacterAppearance source = ResolveAppearance();
            if (source == null) return;

            source.CollectRenderers(_pieceRenderers, false);
            foreach (BoneChainDynamics chain in ResolveChains()) {
                if (chain != null) chain.Weight = IsWorn(TriggerOf(chain)) ? 1f : 0f;
            }
        }

        private void OnEnable() {
            Subscribe();
        }

        private void OnDisable() {
            Unsubscribe();
        }

        private void Subscribe() {
            _subscribed = ResolveAppearance();
            if (_subscribed == null) return;

            _subscribed.PiecesChanged += Refresh;
            Refresh();
        }

        private void Unsubscribe() {
            if (_subscribed != null) _subscribed.PiecesChanged -= Refresh;
            _subscribed = null;
        }

        private CharacterAppearance ResolveAppearance() {
            return appearance != null ? appearance : GetComponent<CharacterAppearance>();
        }

        private BoneChainDynamics[] ResolveChains() {
            return chains != null && chains.Length > 0 ? chains : GetComponentsInChildren<BoneChainDynamics>(true);
        }

        private string TriggerOf(BoneChainDynamics chain) {
            if (!string.IsNullOrEmpty(triggerBone)) return triggerBone;

            string[] names = chain.Chain;
            return names.Length > 0 ? names[0] : null;
        }

        private bool IsWorn(string boneName) {
            if (string.IsNullOrEmpty(boneName)) return false;

            foreach (Renderer renderer in _pieceRenderers) {
                if (renderer is SkinnedMeshRenderer skinned && IsBoundTo(skinned, boneName)) return true;
            }

            return false;
        }

        private static bool IsBoundTo(SkinnedMeshRenderer renderer, string boneName) {
            int index = Array.FindIndex(renderer.bones, (bone) => bone != null && bone.name == boneName);
            if (index < 0) return false;

            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || !mesh.isReadable) return false;

            foreach (BoneWeight1 weight in mesh.GetAllBoneWeights()) {
                if (weight.boneIndex == index && weight.weight > 0f) return true;
            }

            return false;
        }
    }
}
