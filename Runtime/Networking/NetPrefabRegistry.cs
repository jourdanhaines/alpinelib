using System;
using AlpineLib.Netcode.Protocol;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// The shared vocabulary of spawnable networked things: a row's index is the prefab id that travels
    /// in every <c>SpawnEntity</c>.
    /// </summary>
    /// <remarks>
    /// Because the index is the wire id, this list is <b>append-only</b> — reordering or deleting a row
    /// silently repoints every shipped build's spawns at a different prefab, and a retired row must be
    /// left in place (with a null prefab) rather than removed.
    /// </remarks>
    [CreateAssetMenu(fileName = "NetPrefabRegistry", menuName = "AlpineLib/Networking/Net Prefab Registry")]
    public class NetPrefabRegistry : ScriptableObject {
        [Tooltip("Append-only. A row's index is its prefab id on the wire; never reorder or delete.")]
        public NetPrefabEntry[] entries = Array.Empty<NetPrefabEntry>();

        /// <summary>Number of authored rows.</summary>
        public int Count => entries?.Length ?? 0;

        /// <summary>
        /// Finds the prefab a spawn message's prefab id refers to.
        /// </summary>
        /// <returns>The prefab, or null when the id is out of range or its row has none authored.</returns>
        /// <remarks>
        /// An unknown id is a data error, not a protocol error — a client running an older registry than
        /// the server will hit it — so it is logged and answered with null rather than thrown, leaving
        /// the caller to skip one spawn instead of losing the session.
        /// </remarks>
        public GameObject ResolvePrefab(ushort prefabId) {
            if (entries == null || prefabId >= entries.Length) {
                Debug.LogError($"NetPrefabRegistry::ResolvePrefab->Prefab id {prefabId} is not in this registry.");
                return null;
            }

            NetPrefabEntry entry = entries[prefabId];

            if (entry == null || entry.prefab == null) {
                Debug.LogError($"NetPrefabRegistry::ResolvePrefab->Row {prefabId} has no prefab authored.");
                return null;
            }

            return entry.prefab;
        }

        /// <summary>Finds the authored row behind a prefab id, or null when the id is out of range.</summary>
        public NetPrefabEntry FindEntry(ushort prefabId) {
            if (entries == null || prefabId >= entries.Length) return null;

            return entries[prefabId];
        }

        /// <summary>
        /// Builds the movement profile table the shared config indexes by prefab id.
        /// </summary>
        /// <remarks>
        /// Every row produces a profile, including rows with no prefab: the array is indexed by prefab
        /// id, so a hole would shift every id after it.
        /// </remarks>
        public MovementProfile[] BuildMovementProfiles() {
            if (entries == null) return Array.Empty<MovementProfile>();

            var profiles = new MovementProfile[entries.Length];

            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++) {
                NetPrefabEntry entry = entries[entryIndex];
                profiles[entryIndex] = entry != null ? entry.ToProfile() : new MovementProfile();
            }

            return profiles;
        }
    }
}
