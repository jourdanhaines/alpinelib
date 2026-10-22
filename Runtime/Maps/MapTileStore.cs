using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace AlpineLib.Maps {
    /// <summary>
    /// Resident map tiles, one <see cref="Texture2DArray"/> per level (sRGB RGBA8, two mips), sliced by
    /// an LRU <see cref="MapTileSlotPool"/>. Captures arrive through <see cref="Accept"/> (each level's
    /// mips copied on the GPU from the capture's mip chain; persisted levels read back and saved), saved
    /// tiles reload on demand, and cells nobody can supply are announced once through
    /// <see cref="TileMissing"/>. On a null graphics device nothing is uploaded or read back.
    /// </summary>
    public sealed class MapTileStore : IMapTileSink, IDisposable {
        /// <summary>Saved tiles loading at once, at most.</summary>
        public const int MaxLoadsInFlight = 8;

        private readonly MapTileSettings _settings;
        private readonly IMapTileStorage _storage;
        private readonly MapTileSlotPool[] _pools;
        private readonly Texture2DArray[] _arrays;
        private readonly Texture2D[] _uploadTextures;
        private readonly Dictionary<CellCoord, bool>[] _savedCache;
        private readonly HashSet<CellCoord>[] _reportedMissing;
        private readonly HashSet<CellCoord>[] _refused;
        private readonly int[] _roomFrame;
        private readonly int[] _room;
        private readonly HashSet<(int level, CellCoord cell)> _loadingKeys = new HashSet<(int level, CellCoord cell)>();
        private readonly List<(int level, CellCoord cell, Task<byte[]> task)> _loads = new List<(int level, CellCoord cell, Task<byte[]> task)>();
        private readonly List<(int level, CellCoord cell, AsyncGPUReadbackRequest request)> _readbacks = new List<(int level, CellCoord cell, AsyncGPUReadbackRequest request)>();
        private int _frame;

        /// <summary>A store laid out by <paramref name="settings"/>, saving persisted levels to <paramref name="storage"/> (null: nothing saved).</summary>
        public MapTileStore(MapTileSettings settings, IMapTileStorage storage = null) {
            _settings = settings ?? MapTileSettings.Default;
            _storage = storage;
            int levels = _settings.LevelCount;
            _pools = new MapTileSlotPool[levels];
            _arrays = new Texture2DArray[levels];
            _uploadTextures = new Texture2D[levels];
            _savedCache = new Dictionary<CellCoord, bool>[levels];
            _reportedMissing = new HashSet<CellCoord>[levels];
            _refused = new HashSet<CellCoord>[levels];
            _roomFrame = new int[levels];
            _room = new int[levels];
            for (int level = 0; level < levels; level++) {
                _pools[level] = new MapTileSlotPool(_settings.Level(level).Budget);
                _savedCache[level] = new Dictionary<CellCoord, bool>();
                _reportedMissing[level] = new HashSet<CellCoord>();
                _refused[level] = new HashSet<CellCoord>();
                _roomFrame[level] = -1;
            }
        }

        /// <summary>Raised when a cell gains a resident tile (captured or loaded).</summary>
        public event Action<CellCoord> TileAdded;

        /// <summary>Raised once per cell and level when a requested tile is neither resident nor saved; cleared when it arrives or is invalidated.</summary>
        public event Action<CellCoord, int> TileMissing;

        /// <summary>Layout.</summary>
        public MapTileSettings Settings => _settings;

        /// <summary>Changes whenever any level gains or loses a resident tile.</summary>
        public int ResidencyVersion { get; private set; }

        /// <summary>Resident tiles summed over every level.</summary>
        public int TileCount {
            get {
                int total = 0;
                foreach (MapTileSlotPool pool in _pools) total += pool.Count;
                return total;
            }
        }

        /// <summary>Level <paramref name="level"/>'s tile array; null until a tile arrives or on a null device.</summary>
        public Texture2DArray GetArray(int level) {
            return _arrays[level];
        }

        /// <summary>The slice holding <paramref name="cell"/> at <paramref name="level"/>.</summary>
        public bool TryGetSlot(int level, CellCoord cell, out int slot) {
            return _pools[level].TryGet(cell, out slot);
        }

        /// <summary>True when <paramref name="cell"/> has a tile at <paramref name="level"/>, resident or saved.</summary>
        public bool HasTile(CellCoord cell, int level) {
            return _pools[level].Contains(cell) || IsSaved(level, cell);
        }

        /// <summary>True when <paramref name="cell"/> has a tile at any level, resident or saved.</summary>
        public bool HasAnyTile(CellCoord cell) {
            for (int level = 0; level < _pools.Length; level++) {
                if (HasTile(cell, level)) return true;
            }

            return false;
        }

        /// <summary>
        /// Marks every cell of <paramref name="cells"/> in use at <paramref name="finestLevel"/> and every
        /// coarser level this frame: resident tiles are pinned, saved ones start loading, and a cell with
        /// nothing at <paramref name="finestLevel"/> is reported missing.
        /// </summary>
        public void Request(GridRect cells, int finestLevel, int frame) {
            _frame = frame;
            int level = Math.Max(0, Math.Min(finestLevel, _pools.Length - 1));
            foreach (CellCoord cell in cells.Cells()) {
                RequestCell(cell, level);
            }
        }

        /// <inheritdoc />
        public void Accept(CellCoord cell, RenderTexture capture) {
            if (!CaptureRender.CanRender || capture == null) return;

            for (int level = 0; level < _pools.Length; level++) {
                AcceptLevel(cell, capture, level);
            }

            TileAdded?.Invoke(cell);
        }

        /// <summary>Drops <paramref name="cell"/>'s tiles from every level and from storage, so it reads as missing until captured again.</summary>
        public void Invalidate(CellCoord cell) {
            bool changed = false;
            for (int level = 0; level < _pools.Length; level++) {
                changed |= _pools[level].Release(cell);
                _reportedMissing[level].Remove(cell);
                _refused[level].Remove(cell);
                _savedCache[level][cell] = false;
                _storage?.Delete(level, cell);
            }

            DropPending(cell);
            if (changed) ResidencyVersion++;
        }

        /// <summary>Main-thread pump: uploads finished loads and saves finished readbacks.</summary>
        public void Tick() {
            PumpLoads();
            PumpReadbacks();
        }

        /// <summary>Destroys the tile arrays; pending loads and saves are dropped.</summary>
        public void Dispose() {
            for (int level = 0; level < _pools.Length; level++) {
                CaptureRender.Destroy(_arrays[level]);
                CaptureRender.Destroy(_uploadTextures[level]);
                _arrays[level] = null;
                _uploadTextures[level] = null;
            }

            _loads.Clear();
            _loadingKeys.Clear();
            _readbacks.Clear();
        }

        private void RequestCell(CellCoord cell, int finestLevel) {
            for (int level = finestLevel; level < _pools.Length; level++) {
                if (_pools[level].Touch(cell, _frame)) continue;
                if (TryStartLoad(level, cell)) continue;
                if (level != finestLevel) continue;

                RetryRefused(level, cell);
                if (!_reportedMissing[level].Add(cell)) continue;

                TileMissing?.Invoke(cell, level);
            }
        }

        private bool IsSaved(int level, CellCoord cell) {
            if (_storage == null || !_settings.Level(level).Persist) return false;
            if (_savedCache[level].TryGetValue(cell, out bool saved)) return saved;

            saved = _storage.Exists(level, cell);
            _savedCache[level][cell] = saved;
            return saved;
        }

        private bool TryStartLoad(int level, CellCoord cell) {
            if (_loadingKeys.Contains((level, cell))) return true;
            if (!CaptureRender.CanRender || !IsSaved(level, cell)) return false;
            if (_loads.Count >= MaxLoadsInFlight) return true;
            // Saved but no slice to load into: wait rather than load and drop it every frame.
            if (!TryReserveRoom(level)) return true;

            _loadingKeys.Add((level, cell));
            _loads.Add((level, cell, _storage.LoadAsync(level, cell, _settings.Level(level).TileSize)));
            return true;
        }

        private void PumpLoads() {
            for (int index = _loads.Count - 1; index >= 0; index--) {
                (int level, CellCoord cell, Task<byte[]> task) = _loads[index];
                if (!task.IsCompleted) continue;

                _loads.RemoveAt(index);
                _loadingKeys.Remove((level, cell));
                FinishLoad(level, cell, task);
            }
        }

        private void FinishLoad(int level, CellCoord cell, Task<byte[]> task) {
            byte[] rgba = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            if (rgba == null) {
                _savedCache[level][cell] = false;
                return;
            }

            if (!UploadTexels(level, cell, rgba)) return;

            TileAdded?.Invoke(cell);
        }

        private bool UploadTexels(int level, CellCoord cell, byte[] rgba) {
            if (!AcquireSlot(level, cell, out int slot)) return false;

            Texture2D upload = EnsureUploadTexture(level);
            upload.SetPixelData(rgba, 0);
            upload.Apply(true, false);
            for (int mip = 0; mip < MapTileSettings.MipsPerLevel; mip++) {
                Graphics.CopyTexture(upload, 0, mip, EnsureArray(level), slot, mip);
            }

            return true;
        }

        private void AcceptLevel(CellCoord cell, RenderTexture capture, int level) {
            int captureMip = _settings.CaptureMipOf(level);
            if (captureMip + MapTileSettings.MipsPerLevel > capture.mipmapCount) return;
            if (!AcquireSlot(level, cell, out int slot)) {
                _refused[level].Add(cell);
                return;
            }

            Texture2DArray array = EnsureArray(level);
            for (int mip = 0; mip < MapTileSettings.MipsPerLevel; mip++) {
                Graphics.CopyTexture(capture, 0, captureMip + mip, array, slot, mip);
            }

            if (_storage == null || !_settings.Level(level).Persist) return;

            _savedCache[level][cell] = true;
            _readbacks.Add((level, cell, AsyncGPUReadback.Request(capture, captureMip)));
        }

        private bool AcquireSlot(int level, CellCoord cell, out int slot) {
            if (!_pools[level].TryAcquire(cell, _frame, out slot, out _)) return false;

            _reportedMissing[level].Remove(cell);
            _refused[level].Remove(cell);
            ResidencyVersion++;
            return true;
        }

        // A capture refused for want of a slice is reported missing again once a slice frees up.
        private void RetryRefused(int level, CellCoord cell) {
            if (!_refused[level].Contains(cell) || !TryReserveRoom(level)) return;

            _refused[level].Remove(cell);
            _reportedMissing[level].Remove(cell);
        }

        // Slices this frame can still hand out at a level, net of loads already headed for one.
        private bool TryReserveRoom(int level) {
            if (_roomFrame[level] != _frame) {
                _roomFrame[level] = _frame;
                _room[level] = _pools[level].AvailableCount(_frame) - LoadsInFlight(level);
            }

            if (_room[level] <= 0) return false;

            _room[level]--;
            return true;
        }

        private int LoadsInFlight(int level) {
            int count = 0;
            foreach ((int level, CellCoord cell, Task<byte[]> task) load in _loads) {
                if (load.level == level) count++;
            }

            return count;
        }

        // Loads and saves still in flight for an invalidated cell would bring the stale tile back.
        private void DropPending(CellCoord cell) {
            _loads.RemoveAll((load) => load.cell == cell);
            _loadingKeys.RemoveWhere((key) => key.cell == cell);
            _readbacks.RemoveAll((readback) => readback.cell == cell);
        }

        private void PumpReadbacks() {
            for (int index = _readbacks.Count - 1; index >= 0; index--) {
                (int level, CellCoord cell, AsyncGPUReadbackRequest request) = _readbacks[index];
                if (!request.done) continue;

                _readbacks.RemoveAt(index);
                if (request.hasError) continue;

                byte[] rgba = request.GetData<byte>().ToArray();
                _storage.SaveAsync(level, cell, _settings.Level(level).TileSize, rgba);
            }
        }

        private Texture2DArray EnsureArray(int level) {
            if (_arrays[level] != null) return _arrays[level];

            MapTileLevelSettings settings = _settings.Level(level);
            var array = new Texture2DArray(settings.TileSize, settings.TileSize, settings.Budget, GraphicsFormat.R8G8B8A8_SRGB,
                TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels, MapTileSettings.MipsPerLevel) {
                name = $"MapTilesL{level}",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 1,
                hideFlags = HideFlags.DontSave,
            };
            array.Apply(false, true);
            _arrays[level] = array;
            return array;
        }

        private Texture2D EnsureUploadTexture(int level) {
            if (_uploadTextures[level] != null) return _uploadTextures[level];

            int side = _settings.Level(level).TileSize;
            _uploadTextures[level] = new Texture2D(side, side, GraphicsFormat.R8G8B8A8_SRGB, MapTileSettings.MipsPerLevel, TextureCreationFlags.MipChain) {
                name = $"MapTileUploadL{level}",
                hideFlags = HideFlags.DontSave,
            };
            return _uploadTextures[level];
        }
    }
}
