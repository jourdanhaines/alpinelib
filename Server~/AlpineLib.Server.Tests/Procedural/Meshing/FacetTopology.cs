using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    /// <summary>Edge-use checks over a triangle soup, welding corners by exact float bits.</summary>
    public static class FacetTopology {
        /// <summary>True when every undirected edge is used exactly twice, once in each direction.</summary>
        public static bool IsWatertight(FacetMesh mesh) {
            Dictionary<(long, long), int> directed = DirectedEdgeCounts(mesh);
            foreach (KeyValuePair<(long, long), int> edge in directed) {
                if (edge.Value != 1) {
                    return false;
                }
                if (!directed.TryGetValue((edge.Key.Item2, edge.Key.Item1), out int reverse) || reverse != 1) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Number of directed edges that have no reversed partner (open boundary edges).</summary>
        public static int OpenEdgeCount(FacetMesh mesh) {
            Dictionary<(long, long), int> directed = DirectedEdgeCounts(mesh);
            int open = 0;
            foreach (KeyValuePair<(long, long), int> edge in directed) {
                if (!directed.ContainsKey((edge.Key.Item2, edge.Key.Item1))) {
                    open += edge.Value;
                }
            }
            return open;
        }

        private static Dictionary<(long, long), int> DirectedEdgeCounts(FacetMesh mesh) {
            Dictionary<Vector3, long> ids = new Dictionary<Vector3, long>();
            Dictionary<(long, long), int> counts = new Dictionary<(long, long), int>();
            for (int corner = 0; corner < mesh.Positions.Count; corner += 3) {
                long a = IdOf(ids, mesh.Positions[corner]);
                long b = IdOf(ids, mesh.Positions[corner + 1]);
                long c = IdOf(ids, mesh.Positions[corner + 2]);
                Count(counts, a, b);
                Count(counts, b, c);
                Count(counts, c, a);
            }
            return counts;
        }

        private static long IdOf(Dictionary<Vector3, long> ids, Vector3 position) {
            if (ids.TryGetValue(position, out long id)) {
                return id;
            }
            id = ids.Count;
            ids.Add(position, id);
            return id;
        }

        private static void Count(Dictionary<(long, long), int> counts, long from, long to) {
            counts.TryGetValue((from, to), out int count);
            counts[(from, to)] = count + 1;
        }
    }
}
