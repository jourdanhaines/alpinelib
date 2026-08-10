using AlpineLib.Netcode.Protocol;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// Authored transport and timing tuning, converted to the shared <see cref="NetConfig"/> the netcode
    /// assemblies read.
    /// </summary>
    /// <remarks>
    /// The shared config is a plain POCO with properties, which Unity cannot serialize, so the authored
    /// surface is this asset's public fields and <see cref="ToConfig"/> is the one place the two are
    /// mapped. The editor exporter writes the same fields into the JSON the dedicated server loads, so a
    /// client and the server it talks to are tuned from a single asset.
    /// </remarks>
    [CreateAssetMenu(fileName = "NetworkConfig", menuName = "AlpineLib/Networking/Network Config")]
    public class NetworkConfig : ScriptableObject {
        [Header("Identity")]
        [Tooltip("Name folded into the connect key, so builds of different games can never join each other.")]
        public string gameProtocolName = "penguin";

        [Header("Transport")]
        [Tooltip("UDP port the server binds and clients dial.")]
        public int port = 9050;
        [Tooltip("Maximum simultaneous transport connections a server accepts.")]
        public int maxPeers = 16;
        [Tooltip("Milliseconds of silence after which a link is considered dead.")]
        public int disconnectTimeoutMs = 5000;

        [Header("Timing")]
        [Tooltip("Authoritative simulation steps per second.")]
        public int serverTickRate = 30;
        [Tooltip("Snapshots broadcast per second. Lower than the tick rate on purpose: bandwidth, not fidelity, is the constraint.")]
        public int snapshotRate = 15;
        [Tooltip("Input commands an owning client sends per second.")]
        public int clientSendRate = 30;
        [Tooltip("Milliseconds remote pawns are rendered in the past, buying the interpolator two samples to blend between.")]
        public int interpolationDelayMs = 100;

        [Header("Validation")]
        [Tooltip("How far past a gait's top speed a reported movement may run before the server calls it a violation. Owner-authoritative pawns only.")]
        public float movementToleranceMultiplier = 1.5f;

        /// <summary>
        /// Builds the shared config, optionally carrying the movement profiles a prefab registry
        /// authors.
        /// </summary>
        /// <param name="prefabRegistry">
        /// Registry whose entries supply one <see cref="MovementProfile"/> per prefab id, or null when
        /// the caller only needs transport tuning.
        /// </param>
        public NetConfig ToConfig(NetPrefabRegistry prefabRegistry) {
            var config = new NetConfig {
                GameProtocolName = gameProtocolName,
                Port = port,
                MaxPeers = maxPeers,
                DisconnectTimeoutMs = disconnectTimeoutMs,
                ServerTickRate = serverTickRate,
                SnapshotRate = snapshotRate,
                ClientSendRate = clientSendRate,
                InterpolationDelayMs = interpolationDelayMs,
                MovementToleranceMultiplier = movementToleranceMultiplier
            };

            if (prefabRegistry != null) {
                config.MovementProfiles = prefabRegistry.BuildMovementProfiles();
            }

            return config;
        }
    }
}
