using System.Numerics;
using AlpineLib.Netcode.Sessions.Spawning;

namespace AlpineLib.Server.Configuration {
    /// <summary>JSON mirror of one authored spawn point.</summary>
    /// <remarks>
    /// The vector is spelt out component by component rather than as a nested object, because that is
    /// what the Unity exporter writes and a hand-edited file is far easier to read this way.
    /// </remarks>
    public sealed class SpawnPointDocument {
        public float X { get; set; }

        public float Y { get; set; }

        public float Z { get; set; }

        public float YawDegrees { get; set; }

        /// <summary>Maps this document onto the point the placement takes.</summary>
        public SpawnPoint ToPoint() {
            return new SpawnPoint(new Vector3(X, Y, Z), YawDegrees);
        }
    }
}
