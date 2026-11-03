using System.Collections.Generic;
using AlpineLib.Animation;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// Opts a character's corrective blendshape drivers into its worn pieces: whenever the
    /// <see cref="CharacterAppearance"/> rebuilds, every built skinned piece carrying a driver's shape
    /// becomes one of that driver's extra targets, so clothing folds with the body it covers.
    /// </summary>
    /// <remarks>
    /// Matching is by blendshape name only, so any item authored with the body's shape joins and others are
    /// ignored. Drivers default to those on this object. Runs in edit mode too, so previews and gates that
    /// step a driver see the worn pieces.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WornBlendShapeTargets : MonoBehaviour {
        [Tooltip("Appearance whose built pieces join the drivers; this object's when empty.")]
        [SerializeField] private CharacterAppearance appearance;

        [Tooltip("Drivers that take the worn pieces; every driver on this object when empty.")]
        [SerializeField] private BoneAngleBlendShapeDriver[] drivers = new BoneAngleBlendShapeDriver[0];

        private readonly List<Renderer> _pieceRenderers = new List<Renderer>();
        private readonly List<SkinnedMeshRenderer> _matches = new List<SkinnedMeshRenderer>();
        private CharacterAppearance _subscribed;

        /// <summary>Sets the appearance and drivers, e.g. from an assembler; null or empty picks this object's.</summary>
        public void Configure(CharacterAppearance source, BoneAngleBlendShapeDriver[] targetDrivers) {
            Unsubscribe();
            appearance = source;
            drivers = targetDrivers ?? new BoneAngleBlendShapeDriver[0];
            if (isActiveAndEnabled) Subscribe();
        }

        /// <summary>Hands every driver the built skinned pieces carrying its shape. Public so gates can force it.</summary>
        public void Refresh() {
            CharacterAppearance source = ResolveAppearance();
            if (source == null) return;

            source.CollectRenderers(_pieceRenderers, false);
            foreach (BoneAngleBlendShapeDriver driver in ResolveDrivers()) {
                if (driver != null) driver.SetExtraTargets(MatchingPieces(driver.BlendShapeName));
            }
        }

        private void OnEnable() {
            Subscribe();
        }

        private void OnDisable() {
            Unsubscribe();
            foreach (BoneAngleBlendShapeDriver driver in ResolveDrivers()) {
                if (driver != null) driver.SetExtraTargets(null);
            }
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

        private BoneAngleBlendShapeDriver[] ResolveDrivers() {
            return drivers != null && drivers.Length > 0 ? drivers : GetComponents<BoneAngleBlendShapeDriver>();
        }

        private List<SkinnedMeshRenderer> MatchingPieces(string shapeName) {
            _matches.Clear();
            if (string.IsNullOrEmpty(shapeName)) return _matches;

            foreach (Renderer renderer in _pieceRenderers) {
                if (renderer is SkinnedMeshRenderer skinned && HasShape(skinned, shapeName)) _matches.Add(skinned);
            }

            return _matches;
        }

        private static bool HasShape(SkinnedMeshRenderer renderer, string shapeName) {
            return renderer.sharedMesh != null && renderer.sharedMesh.GetBlendShapeIndex(shapeName) >= 0;
        }
    }
}
