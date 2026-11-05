using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Fading;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Fades in everything a root holds with the stylized screen-door: mesh renderers (swapped to fading twins with a
    /// <c>_FadeAmount</c> block, restored once full) and <see cref="InstancedMeshRenderer"/>s (their own schedules).
    /// Things built over several frames are captured right after each step and held hidden, then
    /// <see cref="StartAt"/> fades them all together, each after a small stable delay so a cell does not blink as one.
    /// Renderers whose materials cannot fade are left drawing as they are. Capture cameras (map tiles, portraits) see
    /// everything in full on the renderers' own materials, so a bake never keeps a fade. Property blocks others set
    /// are merged into, never cleared; a renderer whose materials someone else replaces mid-fade leaves the fade.
    /// </summary>
    [ExecuteAlways]
    public sealed class DitherFadeGroup : MonoBehaviour {
        private readonly List<DitherFadeEntry> _renderers = new List<DitherFadeEntry>();
        private readonly HashSet<MeshRenderer> _seen = new HashSet<MeshRenderer>();
        private readonly HashSet<InstancedMeshRenderer> _seenInstanced = new HashSet<InstancedMeshRenderer>();
        private readonly List<FadeInSchedule> _schedules = new List<FadeInSchedule>();
        private readonly List<MeshRenderer> _scratch = new List<MeshRenderer>();
        private readonly List<InstancedMeshRenderer> _scratchInstanced = new List<InstancedMeshRenderer>();
        private readonly List<Material> _scratchMaterials = new List<Material>();
        private MaterialPropertyBlock _block;
        private DitherFadeMaterials _materials;
        private double _duration = 1.0;
        private float _maxOffset;
        private float _maxDelay;
        private Func<double> _clock = DitherFade.DefaultClock;
        private double _start = double.NaN;

        /// <summary>Mesh renderers fading (or held).</summary>
        public int RendererCount => _renderers.Count;

        /// <summary>Instanced renderers given a schedule.</summary>
        public int InstancedCount => _schedules.Count;

        /// <summary>Renderers left as they are because a material cannot fade.</summary>
        public int UnsupportedCount { get; private set; }

        /// <summary>The instanced renderers' schedules, in capture order.</summary>
        public IReadOnlyList<FadeInSchedule> Schedules => _schedules;

        public bool IsStarted => !double.IsNaN(_start);

        /// <summary>True once the mesh renderers are back on their own materials.</summary>
        public bool IsRestored { get; private set; }

        /// <summary>Fades over <paramref name="durationSeconds"/> after delays below <paramref name="maxOffsetSeconds"/>, twins from <paramref name="materials"/>.</summary>
        public void Configure(DitherFadeMaterials materials, double durationSeconds, float maxOffsetSeconds, Func<double> clock) {
            if (!(durationSeconds > 0.0)) throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Fade duration must be positive.");

            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _duration = durationSeconds;
            _maxOffset = Mathf.Max(0f, maxOffsetSeconds);
            _clock = clock ?? DitherFade.DefaultClock;
        }

        /// <summary>Holds every mesh and instanced renderer under <paramref name="root"/> not seen before; returns how many were new.</summary>
        public int Capture(Transform root) {
            if (_materials == null) throw new InvalidOperationException("Configure the fade group before capturing.");

            int captured = 0;
            root.GetComponentsInChildren(true, _scratch);
            foreach (MeshRenderer renderer in _scratch) {
                if (_seen.Add(renderer) && TryHold(root, renderer)) captured++;
            }
            root.GetComponentsInChildren(true, _scratchInstanced);
            foreach (InstancedMeshRenderer renderer in _scratchInstanced) {
                if (_seenInstanced.Add(renderer) && TryHold(renderer)) captured++;
            }
            _scratch.Clear();
            _scratchInstanced.Clear();
            return captured;
        }

        /// <summary>Leaves <paramref name="renderer"/> out of every later capture: it keeps drawing as it is.</summary>
        public void Ignore(Renderer renderer) {
            if (renderer is MeshRenderer meshRenderer) _seen.Add(meshRenderer);
        }

        /// <summary>Begins every held fade at clock time <paramref name="start"/>.</summary>
        public void StartAt(double start) {
            _start = start;
            foreach (FadeInSchedule schedule in _schedules) {
                if (schedule.IsHeld) schedule.StartAt(start);
            }
            Refresh();
        }

        /// <summary>True once every captured thing is fully shown at <paramref name="now"/>.</summary>
        public bool IsDone(double now) {
            if (!IsStarted) return false;
            if (now - _start < _duration + _maxDelay) return false;

            foreach (FadeInSchedule schedule in _schedules) {
                if (!schedule.IsDone(now)) return false;
            }
            return true;
        }

        /// <summary>The fade amount mesh renderer number <paramref name="index"/> draws with at <paramref name="now"/>.</summary>
        public float RendererAmount(int index, double now) {
            if (!IsStarted || IsRestored) return IsRestored ? 1f : 0f;

            return FadeInSchedule.Amount(now - _start - _renderers[index].Delay, _duration);
        }

        /// <summary>Writes the mesh renderers' amounts for the clock's now; restores them once all are full.</summary>
        public void Refresh() {
            if (!IsStarted || IsRestored) return;

            double now = _clock();
            if (now - _start >= _duration + _maxDelay) {
                Restore();
                return;
            }

            for (int index = 0; index < _renderers.Count; index++) {
                DitherFadeEntry entry = _renderers[index];
                if (entry.Renderer == null || entry.IsReleased) continue;
                if (!HasOwnTwins(entry)) {
                    Release(entry);
                    continue;
                }

                SetAmount(entry, FadeInSchedule.Amount(now - _start - entry.Delay, _duration));
            }
        }

        /// <summary>Renderers that left the fade because someone else replaced their materials mid-fade.</summary>
        public int ReleasedCount {
            get {
                int released = 0;
                foreach (DitherFadeEntry entry in _renderers) {
                    if (entry.IsReleased) released++;
                }
                return released;
            }
        }

        private void OnEnable() {
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable() {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) {
            Refresh();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) {
            if (!IsRestored && CaptureCameraSetup.IsCaptureCamera(camera)) SwapMaterials(false);
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera) {
            if (!IsRestored && CaptureCameraSetup.IsCaptureCamera(camera)) SwapMaterials(true);
        }

        private void SwapMaterials(bool toTwins) {
            foreach (DitherFadeEntry entry in _renderers) {
                if (entry.Renderer != null && !entry.IsReleased) entry.Renderer.sharedMaterials = toTwins ? entry.Twins : entry.Originals;
            }
        }

        private bool TryHold(Transform root, MeshRenderer renderer) {
            Material[] originals = renderer.sharedMaterials;
            var twins = new Material[originals.Length];
            if (originals.Length == 0 || !_materials.TryGetTwins(originals, twins)) {
                UnsupportedCount++;
                return false;
            }

            Vector3 local = root.InverseTransformPoint(renderer.transform.position);
            var entry = new DitherFadeEntry(renderer, originals, twins, renderer.HasPropertyBlock(), FadeInSchedule.OffsetFor(local.x, local.z, _maxOffset));
            _maxDelay = Mathf.Max(_maxDelay, entry.Delay);
            renderer.sharedMaterials = twins;
            SetAmount(entry, 0f, true);
            _renderers.Add(entry);
            IsRestored = false;
            return true;
        }

        private bool TryHold(InstancedMeshRenderer renderer) {
            if (renderer.Batch == null || renderer.InstanceCount == 0) return false;
            if (!_materials.TryGetTwin(renderer.Material, out Material twin)) {
                UnsupportedCount++;
                return false;
            }

            FadeInSchedule schedule = renderer.HoldFade(twin, _duration, _maxOffset, _clock);
            if (IsStarted) schedule.StartAt(_start);
            _schedules.Add(schedule);
            return true;
        }

        // Merges into whatever block the renderer holds now; a block without our amount was put there by someone else.
        private void SetAmount(DitherFadeEntry entry, float amount, bool isFirst = false) {
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            entry.Renderer.GetPropertyBlock(_block);
            if (!isFirst && !_block.isEmpty && !_block.HasFloat(DitherFade.AmountId)) entry.HasForeignBlock = true;
            _block.SetFloat(DitherFade.AmountId, amount);
            entry.Renderer.SetPropertyBlock(_block);
        }

        private bool HasOwnTwins(DitherFadeEntry entry) {
            entry.Renderer.GetSharedMaterials(_scratchMaterials);
            bool isOwn = _scratchMaterials.Count == entry.Twins.Length;
            for (int index = 0; isOwn && index < entry.Twins.Length; index++) isOwn = _scratchMaterials[index] == entry.Twins[index];

            _scratchMaterials.Clear();
            return isOwn;
        }

        // Leaves the renderer's materials alone; a foreign block keeps its values (ours reads full), ours goes.
        private void Release(DitherFadeEntry entry) {
            entry.IsReleased = true;
            ClearAmount(entry);
        }

        private void ClearAmount(DitherFadeEntry entry) {
            if (entry.HasForeignBlock) SetAmount(entry, 1f);
            else entry.Renderer.SetPropertyBlock(null);
        }

        private void Restore() {
            foreach (DitherFadeEntry entry in _renderers) {
                if (entry.Renderer == null || entry.IsReleased) continue;
                if (!HasOwnTwins(entry)) {
                    Release(entry);
                    continue;
                }

                SetAmount(entry, 1f);
                entry.Renderer.sharedMaterials = entry.Originals;
                ClearAmount(entry);
            }
            IsRestored = true;
        }
    }
}
