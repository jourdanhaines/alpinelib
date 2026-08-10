using System.Collections.Generic;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The authoritative set of live entities in one session, and the only place entity ids are minted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One registry per session, owned by that session's <see cref="ServerReplication"/>. Ids are unique
    /// within the registry and never reused, so a snapshot that overtakes a despawn cannot resurrect a
    /// dead entity under a live id.
    /// </para>
    /// <para>
    /// Both a list and a dictionary are kept. Snapshot building walks every entity in a stable order
    /// every tick, and message handling looks entities up by id many times a tick; serving both from one
    /// structure would make one of the two the slow path for no benefit.
    /// </para>
    /// </remarks>
    public sealed class ServerEntityRegistry {
        private readonly Dictionary<uint, NetEntity> entitiesById = new Dictionary<uint, NetEntity>();
        private readonly List<NetEntity> entities = new List<NetEntity>();

        private uint nextEntityId = 1u;

        /// <summary>Every live entity, in creation order. Do not mutate.</summary>
        public IReadOnlyList<NetEntity> Entities => entities;

        /// <summary>How many entities are live.</summary>
        public int Count => entities.Count;

        /// <summary>Mints an id and registers a new entity in its spawn state.</summary>
        public NetEntity Create(ushort prefabId, int ownerPeerId, AuthorityMode authority, in PawnState initialState) {
            var entity = new NetEntity(nextEntityId, prefabId, ownerPeerId, authority, in initialState);
            nextEntityId++;

            entitiesById.Add(entity.Id, entity);
            entities.Add(entity);
            return entity;
        }

        /// <summary>Finds a live entity by id.</summary>
        public bool TryGet(uint entityId, out NetEntity entity) {
            return entitiesById.TryGetValue(entityId, out entity);
        }

        /// <summary>Removes an entity, reporting whether it was there.</summary>
        public bool Remove(uint entityId) {
            if (!entitiesById.Remove(entityId)) {
                return false;
            }

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                if (entities[entityIndex].Id != entityId) {
                    continue;
                }

                entities.RemoveAt(entityIndex);
                return true;
            }

            return true;
        }

        /// <summary>
        /// Removes every entity a peer owns, appending their ids to <paramref name="removedIds"/> so the
        /// caller can broadcast the despawns. Used when a player leaves for good.
        /// </summary>
        public void RemoveOwnedBy(int ownerPeerId, List<uint> removedIds) {
            for (int entityIndex = entities.Count - 1; entityIndex >= 0; entityIndex--) {
                NetEntity entity = entities[entityIndex];

                if (entity.OwnerPeerId != ownerPeerId) {
                    continue;
                }

                entities.RemoveAt(entityIndex);
                entitiesById.Remove(entity.Id);
                removedIds?.Add(entity.Id);
            }
        }

        /// <summary>Forgets every entity. Ids continue from where they left off, never restarting.</summary>
        public void Clear() {
            entitiesById.Clear();
            entities.Clear();
        }
    }
}
