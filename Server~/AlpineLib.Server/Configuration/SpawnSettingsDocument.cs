using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Server.Sessions.Spawning;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// JSON mirror of the exported <c>spawn</c> section: what an arriving player is given a body as, and
    /// where that body appears.
    /// </summary>
    /// <remarks>
    /// The section is optional. A config that omits it gets the documented defaults — prefab zero,
    /// server-simulated, on a two-metre ring of eight seats — which is exactly what the library did
    /// before the section existed, so an older export still boots a newer server.
    /// </remarks>
    public sealed class SpawnSettingsDocument {
        public ushort PawnPrefabId { get; set; } = SpawnSettings.DefaultPawnPrefabId;

        public AuthorityMode PawnAuthority { get; set; } = AuthorityMode.Server;

        public SpawnPlacementKind Placement { get; set; } = SpawnPlacementKind.Ring;

        public float RingRadius { get; set; } = RingSpawnPlacement.DefaultRadiusMetres;

        public int RingSeats { get; set; } = RingSpawnPlacement.DefaultSeats;

        public List<SpawnPointDocument> Points { get; set; } = new List<SpawnPointDocument>();

        /// <summary>Maps this document onto the settings a session's spawner runs by.</summary>
        public SpawnSettings ToSettings() {
            return new SpawnSettings {
                PawnPrefabId = PawnPrefabId,
                PawnAuthority = PawnAuthority,
                Placement = Placement,
                RingRadius = RingRadius,
                RingSeats = RingSeats,
                Points = BuildPoints()
            };
        }

        private SpawnPoint[] BuildPoints() {
            if (Points == null || Points.Count == 0) {
                return Array.Empty<SpawnPoint>();
            }

            List<SpawnPoint> points = new List<SpawnPoint>(Points.Count);

            for (int pointIndex = 0; pointIndex < Points.Count; pointIndex++) {
                SpawnPointDocument document = Points[pointIndex];

                if (document != null) {
                    points.Add(document.ToPoint());
                }
            }

            return points.ToArray();
        }
    }
}
