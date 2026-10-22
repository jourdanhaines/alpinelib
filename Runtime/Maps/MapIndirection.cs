using System;
using AlpineLib.Procedural.Grid;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace AlpineLib.Maps {
    /// <summary>
    /// Per-viewport lookup from cell to tile slice: a <see cref="WindowSize"/>² point-sampled half-float
    /// texture addressed toroidally (texel = cell mod size), each channel holding slice + 1 for one level
    /// (0 = no tile). Covers a window of cells around the view; rebuilt only when the window moves or the
    /// store's residency changes.
    /// </summary>
    public sealed class MapIndirection : IDisposable {
        /// <summary>Default window side, cells.</summary>
        public const int DefaultWindowSize = 32;

        /// <summary>Levels encoded, one per colour channel.</summary>
        public const int MaxLevels = 4;

        private readonly ushort[] _texels;
        private Texture2D _texture;
        private GridRect _window;
        private int _builtVersion = -1;
        private bool _hasWindow;

        /// <summary>An indirection over a <paramref name="windowSize"/>² window.</summary>
        public MapIndirection(int windowSize = DefaultWindowSize) {
            if (windowSize <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "Must be positive.");

            WindowSize = windowSize;
            _texels = new ushort[windowSize * windowSize * 4];
        }

        /// <summary>Window side, cells.</summary>
        public int WindowSize { get; }

        /// <summary>Cells the texture currently covers.</summary>
        public GridRect Window => _window;

        /// <summary>The lookup texture; null on a null graphics device or before the first update.</summary>
        public Texture2D Texture => _texture;

        /// <summary>Toroidal texel index of <paramref name="coordinate"/> for a window of <paramref name="windowSize"/>.</summary>
        public static int IndexOf(int coordinate, int windowSize) {
            int index = coordinate % windowSize;
            return index < 0 ? index + windowSize : index;
        }

        /// <summary>
        /// The <paramref name="windowSize"/>² window centred on <paramref name="visible"/>. Throws when the
        /// visible cells do not fit, since toroidal indices would then alias.
        /// </summary>
        public static GridRect WindowAround(GridRect visible, int windowSize) {
            if (visible.Width > windowSize || visible.Height > windowSize) {
                throw new InvalidOperationException($"Visible cells {visible} do not fit a {windowSize}² indirection window.");
            }

            int x = visible.X - (windowSize - visible.Width) / 2;
            int y = visible.Y - (windowSize - visible.Height) / 2;
            return new GridRect(x, y, windowSize, windowSize);
        }

        /// <summary>
        /// Keeps the window around <paramref name="visible"/> (moving it only when the visible cells leave
        /// it) and rebuilds the texels when it moved or <paramref name="store"/>'s residency changed.
        /// True when the texels were rebuilt.
        /// </summary>
        public bool Update(GridRect visible, MapTileStore store) {
            bool moved = !_hasWindow || !_window.Contains(visible);
            if (moved) {
                _window = WindowAround(visible, WindowSize);
                _hasWindow = true;
            }

            if (!moved && store.ResidencyVersion == _builtVersion) return false;

            _builtVersion = store.ResidencyVersion;
            Rebuild(store);
            return true;
        }

        /// <summary>Slice + 1 recorded for <paramref name="cell"/> at <paramref name="level"/> (0 = none), from the CPU copy.</summary>
        public int EntryOf(CellCoord cell, int level) {
            int texel = IndexOf(cell.Y, WindowSize) * WindowSize + IndexOf(cell.X, WindowSize);
            return (int)Mathf.HalfToFloat(_texels[texel * 4 + level]);
        }

        /// <summary>Destroys the texture.</summary>
        public void Dispose() {
            CaptureRender.Destroy(_texture);
            _texture = null;
        }

        private void Rebuild(MapTileStore store) {
            Array.Clear(_texels, 0, _texels.Length);
            int levels = Math.Min(MaxLevels, store.Settings.LevelCount);
            foreach (CellCoord cell in _window.Cells()) {
                int texel = IndexOf(cell.Y, WindowSize) * WindowSize + IndexOf(cell.X, WindowSize);
                WriteTexel(store, cell, texel * 4, levels);
            }

            Upload();
        }

        private void WriteTexel(MapTileStore store, CellCoord cell, int offset, int levels) {
            for (int level = 0; level < levels; level++) {
                if (!store.TryGetSlot(level, cell, out int slot)) continue;

                _texels[offset + level] = Mathf.FloatToHalf(slot + 1);
            }
        }

        private void Upload() {
            if (!CaptureRender.CanRender) return;

            if (_texture == null) {
                _texture = new Texture2D(WindowSize, WindowSize, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.DontInitializePixels) {
                    name = "MapIndirection",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.DontSave,
                };
            }

            _texture.SetPixelData(_texels, 0);
            _texture.Apply(false, false);
        }
    }
}
