using AlpineLib.Procedural.Grid;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineLib.Maps {
    /// <summary>
    /// The map drawn as one UI quad: every <c>LateUpdate</c> it sizes its <see cref="MapView"/> to the
    /// rect, picks the tile level for the zoom, requests the visible cells from the <see cref="MapTileStore"/>,
    /// refreshes its <see cref="MapIndirection"/> and feeds the <c>AlpineLib/UI/MapViewport</c> shader.
    /// Uses its own copy of the base material, so several viewports (page map, minimap) can coexist.
    /// Cells without a tile at any level draw opaque black (the unexplored world).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class MapViewportGraphic : MaskableGraphic {
        private const string CircleKeyword = "_MAP_CIRCLE";

        private static readonly int TilesL0Id = Shader.PropertyToID("_TilesL0");
        private static readonly int TilesL1Id = Shader.PropertyToID("_TilesL1");
        private static readonly int TilesL2Id = Shader.PropertyToID("_TilesL2");
        private static readonly int IndirectionId = Shader.PropertyToID("_Indirection");
        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int MetresPerUnitId = Shader.PropertyToID("_MetresPerUnit");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        private static readonly int CentreFracId = Shader.PropertyToID("_CentreFrac");
        private static readonly int CentreIdxId = Shader.PropertyToID("_CentreIdx");
        private static readonly int WindowSizeId = Shader.PropertyToID("_WindowSize");
        private static readonly int WindowMinId = Shader.PropertyToID("_WindowMin");
        private static readonly int RotId = Shader.PropertyToID("_Rot");
        private static readonly int LevelId = Shader.PropertyToID("_Level");
        private static readonly int LevelLodId = Shader.PropertyToID("_LevelLod");

        [Tooltip("Material on the AlpineLib/UI/MapViewport shader; copied per viewport.")]
        [SerializeField] private Material baseMaterial;

        [Tooltip("Clip to the inscribed circle (round minimap).")]
        [SerializeField] private bool circle;

        [Tooltip("Cells requested beyond the visible edge.")]
        [SerializeField] private int cellMargin = 1;

        private MapView _view;
        private MapTileStore _store;
        private MapIndirection _indirection;
        private Material _instance;

        /// <summary>The view drawn; null until <see cref="Bind"/>.</summary>
        public MapView View => _view;

        /// <summary>The tiles drawn from; null until <see cref="Bind"/>.</summary>
        public MapTileStore Store => _store;

        /// <summary>This viewport's cell → slice lookup.</summary>
        public MapIndirection Indirection => _indirection ??= new MapIndirection();

        /// <summary>True when clipped to the inscribed circle.</summary>
        public bool IsCircle => circle;

        /// <summary>Tile level picked at the last refresh.</summary>
        public int SelectedLevel { get; private set; }

        /// <summary>Screen pixels per map metre at the last refresh.</summary>
        public double PixelsPerMetre { get; private set; }

        /// <summary>Draws <paramref name="view"/> from <paramref name="store"/>.</summary>
        public void Bind(MapView view, MapTileStore store) {
            _view = view;
            _store = store;
        }

        /// <summary>Switches circle clipping.</summary>
        public void SetCircle(bool value) {
            circle = value;
            if (_instance != null) ApplyKeyword(_instance);
            Material rendered = RenderedCopy();
            if (rendered != null) ApplyKeyword(rendered);
        }

        /// <inheritdoc />
        public override Material GetModifiedMaterial(Material baseMaterialIn) {
            Material source = EnsureInstance();
            return base.GetModifiedMaterial(source != null ? source : baseMaterialIn);
        }

        /// <summary>Requests, refreshes and pushes this frame's view to the shader; called from <c>LateUpdate</c>.</summary>
        public void Refresh(int frame) {
            if (_view == null || _store == null) return;

            Vector2 size = rectTransform.rect.size;
            _view.SetViewportSize(size);
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            PixelsPerMetre = scale / _view.MetresPerUnit;
            SelectedLevel = MapTileLevels.Select(PixelsPerMetre, _store.Settings);

            double cellSize = _store.Settings.CellSize;
            int window = Indirection.WindowSize;
            GridRect visible = ClampToWindow(_view.VisibleCells(cellSize, cellMargin), window);
            GridRect inner = ClampToWindow(_view.VisibleCells(cellSize, 0), window);
            // Margin cells prefetch one level coarser so they never crowd on-screen cells out of the finest level's slices.
            _store.Request(visible, Mathf.Min(SelectedLevel + 1, _store.Settings.LevelCount - 1), frame);
            _store.Request(inner, SelectedLevel, frame);
            Indirection.Update(visible, _store);

            Material target = EnsureInstance();
            if (target == null) return;

            ApplyProperties(target, size);
            Material rendered = RenderedCopy();
            if (rendered == null) return;

            ApplyProperties(rendered, size);
            ApplyKeyword(rendered);
        }

        /// <summary>
        /// <paramref name="visible"/> trimmed about its centre to at most <paramref name="windowSize"/>
        /// cells a side; the shader paints cells outside the window black rather than aliasing them.
        /// </summary>
        public static GridRect ClampToWindow(GridRect visible, int windowSize) {
            int width = Mathf.Min(visible.Width, windowSize);
            int height = Mathf.Min(visible.Height, windowSize);
            return new GridRect(visible.X + (visible.Width - width) / 2, visible.Y + (visible.Height - height) / 2, width, height);
        }

        protected virtual void LateUpdate() {
            Refresh(Time.frameCount);
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            _indirection?.Dispose();
            _indirection = null;
            CaptureRender.Destroy(_instance);
            _instance = null;
        }

        protected override void OnPopulateMesh(VertexHelper vertices) {
            vertices.Clear();
            Rect rect = GetPixelAdjustedRect();
            Color32 tint = color;
            vertices.AddVert(new Vector3(rect.xMin, rect.yMin), tint, new Vector4(0f, 0f));
            vertices.AddVert(new Vector3(rect.xMin, rect.yMax), tint, new Vector4(0f, 1f));
            vertices.AddVert(new Vector3(rect.xMax, rect.yMax), tint, new Vector4(1f, 1f));
            vertices.AddVert(new Vector3(rect.xMax, rect.yMin), tint, new Vector4(1f, 0f));
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);
        }

        // The stencil copy a Mask makes of the instance; it keeps the keywords it was copied with.
        private Material RenderedCopy() {
            Material rendered = canvasRenderer.materialCount > 0 ? canvasRenderer.GetMaterial() : null;
            return rendered != null && rendered != _instance ? rendered : null;
        }

        private Material EnsureInstance() {
            if (_instance != null) return _instance;
            if (baseMaterial == null) return null;

            _instance = new Material(baseMaterial) { name = baseMaterial.name + " (Viewport)", hideFlags = HideFlags.DontSave };
            ApplyKeyword(_instance);
            return _instance;
        }

        private void ApplyKeyword(Material target) {
            if (circle) {
                target.EnableKeyword(CircleKeyword);
                return;
            }

            target.DisableKeyword(CircleKeyword);
        }

        private void ApplyProperties(Material target, Vector2 size) {
            MapTileSettings settings = _store.Settings;
            double cellSize = settings.CellSize;
            double cellX = System.Math.Floor(_view.Centre.X / cellSize);
            double cellZ = System.Math.Floor(_view.Centre.Z / cellSize);
            int window = Indirection.WindowSize;
            double radians = _view.RotationDegrees * System.Math.PI / 180.0;

            target.SetTexture(TilesL0Id, _store.GetArray(0));
            target.SetTexture(TilesL1Id, settings.LevelCount > 1 ? _store.GetArray(1) : null);
            target.SetTexture(TilesL2Id, settings.LevelCount > 2 ? _store.GetArray(2) : null);
            target.SetTexture(IndirectionId, Indirection.Texture);
            target.SetVector(RectSizeId, new Vector4(size.x, size.y, 0f, 0f));
            target.SetFloat(MetresPerUnitId, (float)_view.MetresPerUnit);
            target.SetFloat(CellSizeId, (float)cellSize);
            target.SetVector(CentreFracId, new Vector4((float)(_view.Centre.X / cellSize - cellX), (float)(_view.Centre.Z / cellSize - cellZ), 0f, 0f));
            target.SetVector(CentreIdxId, new Vector4(MapIndirection.IndexOf((int)cellX, window), MapIndirection.IndexOf((int)cellZ, window), 0f, 0f));
            target.SetFloat(WindowSizeId, window);
            GridRect covered = Indirection.Window;
            target.SetVector(WindowMinId, new Vector4((float)(covered.X - cellX), (float)(covered.Y - cellZ), 0f, 0f));
            target.SetVector(RotId, new Vector4((float)System.Math.Cos(radians), (float)System.Math.Sin(radians), 0f, 0f));
            target.SetFloat(LevelId, SelectedLevel);
            target.SetVector(LevelLodId, LevelLods(settings));
        }

        private Vector4 LevelLods(MapTileSettings settings) {
            var lods = Vector4.zero;
            for (int level = 0; level < Mathf.Min(settings.LevelCount, 4); level++) {
                lods[level] = MapTileLevels.Lod(level, PixelsPerMetre, settings);
            }

            return lods;
        }
    }
}
