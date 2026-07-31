using UnityEngine;

namespace AlpineLib.Spawning {
    /// <summary>
    /// Data for a single spawn point: what prefab to instantiate and how far from the spawner it
    /// may be placed. Shared by every <see cref="Spawner"/> that references the same asset.
    /// </summary>
    [CreateAssetMenu(fileName = "SpawnConfig", menuName = "AlpineLib/Spawning/Spawn Config")]
    public class SpawnConfig : ScriptableObject {
        [Header("Configuration")]
        public float radius;

        [Header("Prefab")]
        public GameObject spawnActorPrefab;
    }
}
