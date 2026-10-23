using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Maps {
    /// <summary>
    /// Tiles as files under a root folder: <c>L{level}/{x}_{y}.tile</c> in <see cref="MapTileCodec"/>
    /// format. Writes go to a temporary file first and replace the tile in one move, so a crash never
    /// leaves a half-written tile. Only the latest save of a tile lands, and a delete drops saves still
    /// in flight, so overlapping saves never resurrect an older or deleted tile.
    /// </summary>
    public class FileMapTileStorage : IMapTileStorage {
        private readonly string _root;
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _latest = new Dictionary<string, long>();
        private long _ticket;

        /// <summary>Storage rooted at <paramref name="root"/> (created on first save).</summary>
        public FileMapTileStorage(string root) {
            _root = root;
        }

        /// <summary>Root folder.</summary>
        public string Root => _root;

        /// <summary>File path of a tile.</summary>
        public string PathOf(int level, CellCoord cell) {
            string name = string.Format(CultureInfo.InvariantCulture, "{0}_{1}.tile", cell.X, cell.Y);
            return Path.Combine(_root, "L" + level.ToString(CultureInfo.InvariantCulture), name);
        }

        /// <inheritdoc />
        public bool Exists(int level, CellCoord cell) {
            return File.Exists(PathOf(level, cell));
        }

        /// <inheritdoc />
        public Task<byte[]> LoadAsync(int level, CellCoord cell, int side) {
            string path = PathOf(level, cell);
            return Task.Run(() => ReadTile(path, side));
        }

        /// <inheritdoc />
        public Task SaveAsync(int level, CellCoord cell, int side, byte[] rgba) {
            string path = PathOf(level, cell);
            long ticket;
            lock (_gate) {
                ticket = ++_ticket;
                _latest[path] = ticket;
            }

            return Task.Run(() => WriteTile(path, side, rgba, ticket));
        }

        /// <inheritdoc />
        public void Delete(int level, CellCoord cell) {
            string path = PathOf(level, cell);
            lock (_gate) {
                _latest.Remove(path);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static byte[] ReadTile(string path, int side) {
            if (!File.Exists(path)) return null;

            byte[] data;
            try {
                data = File.ReadAllBytes(path);
            } catch (IOException) {
                return null;
            }

            return MapTileCodec.TryDecode(data, side, out byte[] rgba) ? rgba : null;
        }

        private void WriteTile(string path, int side, byte[] rgba, long ticket) {
            byte[] data = MapTileCodec.Encode(side, rgba);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllBytes(temporary, data);
                Commit(path, temporary, ticket);
            } finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        // Moves the written file into place unless a newer save or a delete came after it.
        private void Commit(string path, string temporary, long ticket) {
            lock (_gate) {
                if (!_latest.TryGetValue(path, out long latest) || latest != ticket) return;

                _latest.Remove(path);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
        }
    }
}
