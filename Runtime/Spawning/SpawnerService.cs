using System;
using AlpineLib.DI;
using UnityEngine;

namespace AlpineLib.Spawning {
    /// <summary>
    /// Scene level access to spawning: typed spawner lookups, spawning through a placed
    /// <see cref="Spawner"/>, and direct spawning from a <see cref="SpawnConfig"/> at an explicit
    /// pose. Games react to spawned objects through <see cref="OnSpawned"/>.
    /// </summary>
    public interface ISpawnerService {
        /// <summary>
        /// Raised after any successful spawn. The spawner argument is the origin of the spawn, or
        /// null when the object came from <see cref="Spawn"/> at an explicit pose.
        /// </summary>
        event Action<Spawner, GameObject> OnSpawned;

        /// <summary>
        /// Returns every active spawner of the requested type in the loaded scenes. Marker
        /// subclasses of <see cref="Spawner"/> act as the filter.
        /// </summary>
        T[] GetSpawners<T>() where T : Spawner;

        /// <summary>
        /// Spawns the given spawner's configured prefab at a random point in its radius.
        /// </summary>
        GameObject SpawnAtSpawner(Spawner spawner);

        /// <summary>
        /// Spawns a config's prefab at an explicit pose, ignoring spawner placement rules.
        /// </summary>
        GameObject Spawn(SpawnConfig config, Vector3 position, Quaternion rotation);
    }

    /// <inheritdoc cref="ISpawnerService"/>
    public class SpawnerService : MonoBehaviour, ISpawnerService, IDependencyProvider {
        /// <summary>
        /// Fires every spawner in the scene once on Start.
        /// </summary>
        [SerializeField] private bool spawnAllOnStart = true;

        public event Action<Spawner, GameObject> OnSpawned;

        [Provide]
        public ISpawnerService ProvideSpawnerService() {
            return this;
        }

        private void Start() {
            if (!spawnAllOnStart) return;

            var spawners = GetSpawners<Spawner>();
            foreach (var spawner in spawners) {
                SpawnAtSpawner(spawner);
            }
        }

        public T[] GetSpawners<T>() where T : Spawner {
            return FindObjectsByType<T>(FindObjectsSortMode.InstanceID);
        }

        public GameObject SpawnAtSpawner(Spawner spawner) {
            var spawned = spawner.Spawn();
            if (spawned == null) return null;

            OnSpawned?.Invoke(spawner, spawned);

            return spawned;
        }

        public GameObject Spawn(SpawnConfig config, Vector3 position, Quaternion rotation) {
            var spawned = Instantiate(config.spawnActorPrefab, position, rotation);

            OnSpawned?.Invoke(null, spawned);

            return spawned;
        }
    }
}
