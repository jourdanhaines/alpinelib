using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// One keyed batch of static shapes inside a <see cref="CollisionWorld"/>: the shapes as given, plus
    /// the offset that places them in the world.
    /// </summary>
    internal sealed class CollisionGroup {
        public CollisionGroup(ulong key, CollisionShape[] localShapes, Vector3 offset) {
            Key = key;
            LocalShapes = localShapes;
            Offset = offset;
        }

        /// <summary>Sort key; lower keys come first in every query.</summary>
        public ulong Key { get; }

        /// <summary>The shapes relative to <see cref="Offset"/>. Owned by the group, never mutated.</summary>
        public CollisionShape[] LocalShapes { get; }

        /// <summary>Where the group's local origin sits in the world right now.</summary>
        public Vector3 Offset { get; set; }
    }
}
