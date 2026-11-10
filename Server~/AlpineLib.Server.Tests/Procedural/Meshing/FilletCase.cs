using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    /// <summary>One recorded fillet call: the band plan, each vertex's radius and midpoint flag, and hashes of the game's result.</summary>
    public sealed class FilletCase {
        public FilletCase(List<PlanPoint> polygon, double[] radii, string midpoints, ulong roundedHash, ulong maxRadiusHash) {
            Polygon = polygon;
            Corners = new List<FilletCorner>(radii.Length);
            for (int index = 0; index < radii.Length; index++) Corners.Add(new FilletCorner(radii[index], midpoints[index] == '1'));
            RoundedHash = roundedHash;
            MaxRadiusHash = maxRadiusHash;
        }

        public List<PlanPoint> Polygon { get; }

        public List<FilletCorner> Corners { get; }

        /// <summary>Hash of the game's rounded plan.</summary>
        public ulong RoundedHash { get; }

        /// <summary>Hash of every vertex's largest radius at half-edge share.</summary>
        public ulong MaxRadiusHash { get; }
    }
}
