using System;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// Everything one scene's simulation needs to know about its own shape: a name, a content hash, the
    /// static primitives and the movers.
    /// </summary>
    /// <remarks>
    /// This is the exported artefact — what the Unity exporter writes and what a dedicated server loads
    /// from <c>config/geometry/&lt;sceneName&gt;.geo</c>. It is plain data with no behaviour beyond
    /// carrying itself around; <see cref="CollisionWorld"/> is what turns it into something queryable.
    ///
    /// <see cref="ContentHash"/> is over the shapes and movers only, never over the name, so the same
    /// geometry exported twice hashes the same and a mismatch between the client's asset and the
    /// server's file is a real difference in the world rather than a difference in how it was saved.
    /// </remarks>
    public sealed class SceneGeometry {
        /// <summary>Creates a scene's geometry from its parts.</summary>
        public SceneGeometry(string sceneName, uint contentHash, CollisionShape[] staticShapes, MoverDefinition[] movers) {
            SceneName = sceneName ?? string.Empty;
            ContentHash = contentHash;
            StaticShapes = staticShapes ?? Array.Empty<CollisionShape>();
            Movers = movers ?? Array.Empty<MoverDefinition>();
        }

        /// <summary>Unity scene name this geometry was exported from. The key both ends resolve by.</summary>
        public string SceneName { get; }

        /// <summary>FNV-1a over the shapes and movers, for verifying that both ends loaded the same world.</summary>
        public uint ContentHash { get; }

        /// <summary>Immovable primitives, in export order. Index order is the collision iteration order.</summary>
        public CollisionShape[] StaticShapes { get; }

        /// <summary>Moving platforms, in export order. Index order is the mover index everything else uses.</summary>
        public MoverDefinition[] Movers { get; }
    }
}
