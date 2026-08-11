namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// One authored moving platform: an identity, the prefab that renders it, the volume it occupies and
    /// the route it takes.
    /// </summary>
    /// <remarks>
    /// Movers ship inside the scene geometry rather than being replicated as authored data, because both
    /// ends must be able to evaluate the pose themselves. They are nonetheless real replicated entities
    /// as well — spawned with <c>EntityKind.Mover</c> and <c>AuxId</c> set to <see cref="MoverId"/> — so a
    /// client that cannot resolve the definition (a scene it has not loaded, a build older than the
    /// export) still sees the platform move, just interpolated instead of predicted.
    ///
    /// <see cref="LocalShape"/> is authored around the origin and translated by the path's evaluated
    /// position; v1 movers translate only, which is what lets the rider rule be a single vector add.
    /// </remarks>
    public sealed class MoverDefinition {
        /// <summary>Creates a mover definition.</summary>
        public MoverDefinition(ushort moverId, ushort prefabId, in CollisionShape localShape, MoverPath path) {
            MoverId = moverId;
            PrefabId = prefabId;
            LocalShape = localShape;
            Path = path;
        }

        /// <summary>Scene-unique id, authored in Unity. Travels as the entity's <c>AuxId</c>.</summary>
        public ushort MoverId { get; }

        /// <summary>Row in the net prefab registry that renders this mover on clients.</summary>
        public ushort PrefabId { get; }

        /// <summary>Collision volume, authored around the origin and translated by the path.</summary>
        public CollisionShape LocalShape { get; }

        /// <summary>The route, evaluated purely from the simulation tick.</summary>
        public MoverPath Path { get; }
    }
}
