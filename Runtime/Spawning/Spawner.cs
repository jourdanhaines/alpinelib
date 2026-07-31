using UnityEngine;

namespace AlpineLib.Spawning {
    /// <summary>
    /// Scene placed spawn point. Picks a random position inside the radius of its
    /// <see cref="SpawnConfig"/>, optionally snaps that position to the ground with a downward
    /// raycast, and instantiates the configured prefab there. Games filter spawners by declaring
    /// empty marker subclasses (PlayerSpawner, BossSpawner, ...) and querying them through
    /// <see cref="ISpawnerService.GetSpawners{T}"/>.
    /// </summary>
    public class Spawner : MonoBehaviour {
        public SpawnConfig config;

        [Header("Ground Snapping")]
        [SerializeField] private bool snapToGround = true;

        /// <summary>
        /// Layers the ground raycast is allowed to hit. Defaults to Unity's implicit raycast mask,
        /// which is everything except the Ignore Raycast layer.
        /// </summary>
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;

        /// <summary>
        /// Height above the candidate position the ground raycast starts from.
        /// </summary>
        [SerializeField] private float raycastHeight = 50f;

        /// <summary>
        /// Distance the ground raycast travels downward from its origin.
        /// </summary>
        [SerializeField] private float raycastLength = 100f;

        /// <summary>
        /// Instantiates the configured prefab at a random point inside the config radius with the
        /// given rotation. Returns null when no config is assigned.
        /// </summary>
        public GameObject Spawn(Quaternion rotation) {
            if (config == null) {
                Debug.LogError($"Spawner::Spawn->No spawn config found for {this.name}.");
                return null;
            }

            Vector2 offset = Random.insideUnitCircle * config.radius;
            Vector3 spawnPosition = transform.position + new Vector3(offset.x, 0, offset.y);

            if (snapToGround && Physics.Raycast(spawnPosition + Vector3.up * raycastHeight, Vector3.down, out RaycastHit hit, raycastLength, groundLayers)) {
                spawnPosition.y = hit.point.y;
            }

            return Instantiate(config.spawnActorPrefab, spawnPosition, rotation);
        }

        /// <summary>
        /// Instantiates the configured prefab using this spawner's own rotation.
        /// </summary>
        public GameObject Spawn() {
            return this.Spawn(this.transform.rotation);
        }

        /// <summary>
        /// Spawns and returns the requested component from the spawned object, or null when the
        /// spawn failed or the prefab carries no such component.
        /// </summary>
        public T Spawn<T>() where T : Component {
            var spawned = this.Spawn();
            if (spawned == null) return null;

            return spawned.GetComponent<T>();
        }

        private void OnDrawGizmos() {
            if (config == null) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, config.radius);
        }
    }
}
