using AlpineLib.Netcode.Replication;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// The link between a replicated entity and the scene object standing in for it.
    /// </summary>
    /// <remarks>
    /// Whoever instantiates a spawn binds this component to the entity it came from, and everything
    /// that follows — the owner's input sync, a remote pawn's controller, gameplay code looking up who
    /// a hit belongs to — reads the binding from here rather than being handed ids separately. It is
    /// deliberately passive: it holds the binding and nothing else, so a scene object can carry it
    /// whether it is a pawn, a prop or a vehicle.
    /// </remarks>
    public class NetEntityView : MonoBehaviour {
        /// <summary>Replicated entity this object stands for, or null before it is bound.</summary>
        public NetEntity Entity { get; private set; }

        /// <summary>Network id of the bound entity, or zero while unbound.</summary>
        public uint EntityId => Entity?.Id ?? 0u;

        /// <summary>Registry row the bound entity was instantiated from.</summary>
        public ushort PrefabId => Entity?.PrefabId ?? (ushort)0;

        /// <summary>Peer that owns the bound entity, or <c>-1</c> when nobody does.</summary>
        public int OwnerPeerId => Entity?.OwnerPeerId ?? -1;

        /// <summary>Who simulates the bound entity.</summary>
        public AuthorityMode Authority => Entity?.Authority ?? AuthorityMode.Server;

        /// <summary>True when the local player owns this entity.</summary>
        public bool IsOwned { get; private set; }

        /// <summary>True once an entity has been bound.</summary>
        public bool IsBound => Entity != null;

        /// <summary>
        /// Binds this object to a replicated entity.
        /// </summary>
        /// <param name="entity">The entity this object stands for.</param>
        /// <param name="isOwned">True when the local player owns it.</param>
        public void Bind(NetEntity entity, bool isOwned) {
            if (entity == null) {
                Debug.LogError($"NetEntityView::Bind->{name} was handed no entity.");
                return;
            }

            Entity = entity;
            IsOwned = isOwned;
        }

        /// <summary>Drops the binding, leaving the object inert. Used when a session is torn down.</summary>
        public void Unbind() {
            Entity = null;
            IsOwned = false;
        }
    }
}
