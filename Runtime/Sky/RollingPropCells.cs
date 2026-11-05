using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    // The streamed cells a RollingPropField's props roll over: each cell's world part and root (local origin at its
    // centre, roots only translate), a version bumped on every change and the immutable world snapshot builds read.
    internal sealed class RollingPropCells {
        private readonly Dictionary<(long X, long Z), IRollerCell> _cells = new Dictionary<(long X, long Z), IRollerCell>();
        private readonly Dictionary<(long X, long Z), Transform> _roots = new Dictionary<(long X, long Z), Transform>();
        private RollerCellWorld _world;
        private int _worldVersion = -1;

        public int Count => _cells.Count;

        /// <summary>Bumped whenever a cell registers or leaves.</summary>
        public int Version { get; private set; }

        public IEnumerable<(long X, long Z)> Keys => _cells.Keys;

        public void Register(long cellX, long cellZ, Transform root, IRollerCell cell) {
            _cells[(cellX, cellZ)] = cell;
            _roots[(cellX, cellZ)] = root;
            Version++;
        }

        public bool Unregister(long cellX, long cellZ) {
            if (!_cells.Remove((cellX, cellZ))) return false;

            _roots.Remove((cellX, cellZ));
            Version++;
            return true;
        }

        public void Clear() {
            _cells.Clear();
            _roots.Clear();
            _world = null;
            Version++;
        }

        public bool Contains(long cellX, long cellZ) {
            return _cells.ContainsKey((cellX, cellZ));
        }

        /// <summary>The world of the cells as they are now (rebuilt only after a change).</summary>
        public RollerCellWorld WorldOf(double cellSize, double blockReach) {
            if (_world != null && _worldVersion == Version) return _world;

            _world = new RollerCellWorld(cellSize, _cells, blockReach);
            _worldVersion = Version;
            return _world;
        }

        /// <summary>The camera's position in the absolute plane, through any live cell root.</summary>
        public bool TryPlane(Vector3 position, double cellSize, out double x, out double z) {
            foreach (KeyValuePair<(long X, long Z), Transform> pair in _roots) {
                if (pair.Value == null) continue;

                Vector3 origin = pair.Value.position;
                x = RollerCellWorld.CentreOf(pair.Key.X, cellSize) + (position.x - origin.x);
                z = RollerCellWorld.CentreOf(pair.Key.Z, cellSize) + (position.z - origin.z);
                return true;
            }
            x = z = 0.0;
            return false;
        }

        /// <summary>The live root of the cell absolute (x, z) lies in and the point in its local plane (y 0).</summary>
        public bool TryLocal(double x, double z, double cellSize, out Transform root, out Vector3 local) {
            RollerCellWorld.CellOf(x, z, cellSize, out long cellX, out long cellZ);
            local = new Vector3((float)(x - RollerCellWorld.CentreOf(cellX, cellSize)), 0f, (float)(z - RollerCellWorld.CentreOf(cellZ, cellSize)));
            return _roots.TryGetValue((cellX, cellZ), out root) && root != null && root.gameObject.activeInHierarchy;
        }

        /// <summary>True when the cell absolute (x, z) lies in is registered.</summary>
        public bool IsLoadedAt(double x, double z, double cellSize) {
            RollerCellWorld.CellOf(x, z, cellSize, out long cellX, out long cellZ);
            return _cells.ContainsKey((cellX, cellZ));
        }
    }
}
