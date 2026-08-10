using System;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Plain settings object for the networking layer. Authored in Unity as a <c>NetworkConfig</c>
    /// ScriptableObject, exported to JSON for the dedicated server, and handed to the transport, the
    /// facades, the clock and the validator. It carries no behaviour beyond deriving intervals from
    /// rates, and — critically — no Unity and no JSON attributes, so the same instance shape is valid on
    /// both sides of the wire.
    /// </summary>
    public sealed class NetConfig {
        /// <summary>Feeds <see cref="NetProtocol.BuildConnectKey"/>; must match on client and server.</summary>
        public string GameProtocolName { get; set; } = "penguin";

        public int Port { get; set; } = 9050;

        public int MaxPeers { get; set; } = 16;

        /// <summary>Fixed simulation rate of the authoritative game loop, in ticks per second.</summary>
        public int ServerTickRate { get; set; } = 30;

        /// <summary>How often the server broadcasts world snapshots, in packets per second.</summary>
        public int SnapshotRate { get; set; } = 15;

        /// <summary>How often an owning client sends input (or state, in OwnerClient mode).</summary>
        public int ClientSendRate { get; set; } = 30;

        /// <summary>
        /// How far behind the estimated server clock remote pawns are rendered. Buys the interpolator a
        /// buffer of received snapshots so ordinary jitter never turns into a visible stall.
        /// </summary>
        public int InterpolationDelayMs { get; set; } = 100;

        public int DisconnectTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Slack the movement validator allows over a gait's top speed before calling a move a violation.
        /// Absorbs quantization, frame-time variance and slope assist without opening the door to speed
        /// hacks.
        /// </summary>
        public float MovementToleranceMultiplier { get; set; } = 1.5f;

        /// <summary>
        /// Movement envelopes indexed by prefab id, mirroring the Unity prefab registry's ordering. The
        /// index is a wire contract: entries are append-only and never reordered.
        /// </summary>
        public MovementProfile[] MovementProfiles { get; set; } = Array.Empty<MovementProfile>();

        /// <summary>Seconds per authoritative tick.</summary>
        public float ServerTickInterval => SafeInterval(ServerTickRate);

        /// <summary>Seconds between snapshot broadcasts.</summary>
        public float SnapshotInterval => SafeInterval(SnapshotRate);

        /// <summary>Seconds between client sends.</summary>
        public float ClientSendInterval => SafeInterval(ClientSendRate);

        /// <summary>Interpolation delay expressed in seconds, which is what the clock works in.</summary>
        public double InterpolationDelaySeconds => InterpolationDelayMs / 1000.0;

        /// <summary>The connect key both ends must present, derived from the one version constant.</summary>
        public string BuildConnectKey() {
            return NetProtocol.BuildConnectKey(GameProtocolName);
        }

        /// <summary>
        /// Movement envelope for a prefab id, or null when the registry has no entry — callers decide
        /// whether a missing profile means "unvalidated" or "reject", so this does not invent defaults.
        /// </summary>
        public MovementProfile GetMovementProfile(int prefabId) {
            if (MovementProfiles == null || prefabId < 0 || prefabId >= MovementProfiles.Length) {
                return null;
            }

            return MovementProfiles[prefabId];
        }

        private static float SafeInterval(int rate) {
            if (rate <= 0) {
                throw new InvalidOperationException("NetConfig rates must be positive.");
            }

            return 1f / rate;
        }
    }
}
