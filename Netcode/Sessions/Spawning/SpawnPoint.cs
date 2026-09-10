using System.Numerics;

namespace AlpineLib.Netcode.Sessions.Spawning {
    /// <summary>
    /// One authored place a pawn may appear: where it stands and which way it faces.
    /// </summary>
    /// <remarks>
    /// The height is the nominal spawn plane rather than the final answer. A placement probes the
    /// collision world around it, because an authored marker is placed in the editor against the scene's
    /// visual floor while the server only knows the exported geometry, and the two disagree by a few
    /// centimetres more often than not.
    /// </remarks>
    public readonly struct SpawnPoint {
        /// <summary>Builds a point facing along +Z.</summary>
        public SpawnPoint(Vector3 position)
            : this(position, 0f) { }

        /// <summary>Builds a point with an authored facing.</summary>
        public SpawnPoint(Vector3 position, float yawDegrees) {
            Position = position;
            YawDegrees = yawDegrees;
        }

        /// <summary>Where the pawn stands, in world space.</summary>
        public Vector3 Position { get; }

        /// <summary>Which way it faces, in degrees about +Y.</summary>
        public float YawDegrees { get; }
    }
}
