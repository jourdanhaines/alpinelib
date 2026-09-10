using System.Collections.Generic;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions.Spawning;
using UnityEngine;
using SimVector3 = System.Numerics.Vector3;

namespace AlpineLib.Sessions {
    /// <summary>
    /// What body a joining player is given and where it appears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authored once and read three times: by a listen host that builds the placement directly, by the
    /// exporter that flattens it into the dedicated server's JSON, and by whoever reads the asset to
    /// find out which prefab id the session hands out. A single asset is the only way a listen host and
    /// a dedicated server can be relied on to seat the same players in the same places.
    /// </para>
    /// <para>
    /// Both placements' settings live on one asset rather than on a subclass per kind, because a scene
    /// usually gains authored points long after it has been played on a ring, and switching an enum is a
    /// far smaller edit than re-pointing every reference at a new asset.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "SpawnPlacementConfig", menuName = "AlpineLib/Networking/Spawn Placement Config")]
    public class SpawnPlacementConfig : ScriptableObject {
        /// <summary>Prefab id handed out when a session has no placement config at all.</summary>
        public const ushort DefaultPawnPrefabId = 0;

        [Header("Pawn")]
        [Tooltip("Row in the prefab registry that a joining player's body is spawned from.")]
        public ushort pawnPrefabId = DefaultPawnPrefabId;
        [Tooltip("Who simulates the pawn. Server is the default; OwnerClient trades authority for responsiveness.")]
        public AuthorityMode pawnAuthority = AuthorityMode.Server;

        [Header("Placement")]
        [Tooltip("Ring seats arrivals around the origin; List hands out the authored points below.")]
        public SpawnPlacementKind placement = SpawnPlacementKind.Ring;

        [Header("Ring")]
        [Tooltip("How far from the origin the ring's seats sit, in metres.")]
        public float ringRadius = RingSpawnPlacement.DefaultRadiusMetres;
        [Tooltip("Seats on the ring before positions repeat. Usually the lobby capacity.")]
        public int ringSeats = RingSpawnPlacement.DefaultSeats;

        [Header("List")]
        [Tooltip("Places arrivals take in turn. Extras ring the point they share rather than stacking on it.")]
        public SpawnPointEntry[] points;

        /// <summary>
        /// Builds the placement this asset describes.
        /// </summary>
        /// <remarks>
        /// A list placement with no authored points falls back to a ring rather than throwing: the asset
        /// is usually switched to List before the markers have been placed, and a session that opens on
        /// a ring is far easier to diagnose than one that refuses to open at all.
        /// </remarks>
        public ISpawnPlacement ToPlacement() {
            if (placement != SpawnPlacementKind.List) return BuildRingPlacement();

            List<SpawnPoint> spawnPoints = BuildSpawnPoints();

            if (spawnPoints.Count > 0) return new ListSpawnPlacement(spawnPoints);

            Debug.LogWarning(
                $"SpawnPlacementConfig::ToPlacement->{name} places by list but authors no points; using a ring instead.");
            return BuildRingPlacement();
        }

        /// <summary>The authored points as the netcode's engine-free form, skipping empty rows.</summary>
        public List<SpawnPoint> BuildSpawnPoints() {
            var spawnPoints = new List<SpawnPoint>();

            if (points == null) return spawnPoints;

            foreach (SpawnPointEntry entry in points) {
                if (entry == null) continue;

                spawnPoints.Add(new SpawnPoint(
                    new SimVector3(entry.position.x, entry.position.y, entry.position.z),
                    entry.yawDegrees));
            }

            return spawnPoints;
        }

        private RingSpawnPlacement BuildRingPlacement() {
            return new RingSpawnPlacement(ringRadius, Mathf.Max(1, ringSeats));
        }
    }
}
