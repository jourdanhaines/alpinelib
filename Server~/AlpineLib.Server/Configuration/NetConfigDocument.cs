using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// JSON mirror of <see cref="NetConfig"/>, matching the <c>net</c> object the Unity exporter writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shared <see cref="NetConfig"/> deliberately carries no serialisation attributes — it compiles
    /// into Unity, where a System.Text.Json dependency would not be welcome, and it travels the game
    /// wire in binary. So the JSON shape lives here, in net10-only code, and is mapped across by hand.
    /// </para>
    /// <para>
    /// Every property has the shipped default, because a file exported by an older editor is missing
    /// keys rather than broken. Computed values — the intervals, a profile's top speed — are absent from
    /// the export on purpose: both sides derive them from what is written here, so they cannot disagree.
    /// </para>
    /// </remarks>
    public sealed class NetConfigDocument {
        /// <summary>
        /// Name both ends fold into the connect key. Deliberately not the shared default: a server that
        /// booted with no config would otherwise answer a real game's clients.
        /// </summary>
        public string GameProtocolName { get; set; } = "alpine";

        public int Port { get; set; } = 9050;

        public int MaxPeers { get; set; } = 16;

        public int ServerTickRate { get; set; } = 30;

        public int SnapshotRate { get; set; } = 15;

        public int ClientSendRate { get; set; } = 30;

        public int InterpolationDelayMs { get; set; } = 100;

        public int InterpolationDelayMinMs { get; set; } = 60;

        public int InterpolationDelayMaxMs { get; set; } = 250;

        public int DisconnectTimeoutMs { get; set; } = 5000;

        public float MovementToleranceMultiplier { get; set; } = 1.5f;

        /// <summary>
        /// Movement envelopes in prefab-id order, flattened from the Unity prefab registry.
        /// </summary>
        /// <remarks>
        /// The index is a wire contract: the registry is append-only in the editor and the export
        /// preserves its order, so entry <c>n</c> here is what prefab id <c>n</c> on the wire moves by.
        /// </remarks>
        public List<MovementProfileDocument> MovementProfiles { get; set; } = new List<MovementProfileDocument>();

        /// <summary>Maps this document onto the shared config the transport and the motor read.</summary>
        public NetConfig ToConfig() {
            return new NetConfig {
                GameProtocolName = GameProtocolName,
                Port = Port,
                MaxPeers = MaxPeers,
                ServerTickRate = ServerTickRate,
                SnapshotRate = SnapshotRate,
                ClientSendRate = ClientSendRate,
                InterpolationDelayMs = InterpolationDelayMs,
                InterpolationDelayMinMs = InterpolationDelayMinMs,
                InterpolationDelayMaxMs = InterpolationDelayMaxMs,
                DisconnectTimeoutMs = DisconnectTimeoutMs,
                MovementToleranceMultiplier = MovementToleranceMultiplier,
                MovementProfiles = BuildMovementProfiles()
            };
        }

        private MovementProfile[] BuildMovementProfiles() {
            if (MovementProfiles == null || MovementProfiles.Count == 0) {
                return Array.Empty<MovementProfile>();
            }

            MovementProfile[] profiles = new MovementProfile[MovementProfiles.Count];

            for (int profileIndex = 0; profileIndex < MovementProfiles.Count; profileIndex++) {
                profiles[profileIndex] = ResolveProfile(MovementProfiles[profileIndex]);
            }

            return profiles;
        }

        private static MovementProfile ResolveProfile(MovementProfileDocument document) {
            // A missing entry still has to occupy its index, or every prefab id after it shifts by one
            // and the wire contract quietly breaks. Shipped defaults are the honest filler.
            if (document == null) {
                return new MovementProfileDocument().ToProfile();
            }

            return document.ToProfile();
        }
    }
}
