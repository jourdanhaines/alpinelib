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
        /// Where the adaptive interpolation delay starts, in milliseconds: how far behind the estimated
        /// server clock remote pawns are rendered before the timeline has measured anything. The live
        /// value then tracks latency and jitter between <see cref="InterpolationDelayMinMs"/> and
        /// <see cref="InterpolationDelayMaxMs"/>.
        /// </summary>
        public int InterpolationDelayMs { get; set; } = 100;

        /// <summary>Floor of the adaptive interpolation delay, in milliseconds.</summary>
        public int InterpolationDelayMinMs { get; set; } = 60;

        /// <summary>
        /// Ceiling of the adaptive interpolation delay, in milliseconds. Past this, staleness costs more
        /// than the occasional extrapolated frame it would prevent.
        /// </summary>
        public int InterpolationDelayMaxMs { get; set; } = 250;

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

        /// <summary>Initial interpolation delay expressed in seconds, which is what the clock works in.</summary>
        public double InterpolationDelaySeconds => InterpolationDelayMs / 1000.0;

        /// <summary>Adaptive delay floor in seconds.</summary>
        public double InterpolationDelayMinSeconds => InterpolationDelayMinMs / 1000.0;

        /// <summary>Adaptive delay ceiling in seconds.</summary>
        public double InterpolationDelayMaxSeconds => InterpolationDelayMaxMs / 1000.0;

        /// <summary>The connect key both ends must present, derived from the one version constant.</summary>
        public string BuildConnectKey() {
            return NetProtocol.BuildConnectKey(GameProtocolName);
        }

        /// <summary>
        /// Checks the rates against each other and throws when the pair cannot produce a correct
        /// simulation. Called once when a config is loaded, not per tick.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The client must send exactly once per server tick.</b> An owning client predicts one motor
        /// step per input it sends, and the server steps that input once per tick; anything else and the
        /// two integrate a different number of times over the same wall-clock second. Sending slower
        /// leaves the server catching up several ticks on one input — the prediction is ahead by the
        /// difference and every snapshot corrects it — while sending faster queues input the server never
        /// consumes at the rate it arrives, which grows an unbounded backlog and lags the pawn behind its
        /// own keyboard. Neither shows up as an error: both show up as a pawn that will not stop
        /// rubber-banding, which is why the equality is asserted here rather than trusted to authoring.
        /// </para>
        /// <para>
        /// The snapshot rate is deliberately <i>not</i> tied to the tick rate. Snapshots are a broadcast
        /// budget and the interpolator is built to smooth between them at any spacing, so halving them is
        /// a bandwidth decision rather than a simulation one.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">A rate is not positive, or the send rate and the tick rate disagree.</exception>
        public void Validate() {
            if (ServerTickRate <= 0 || ClientSendRate <= 0 || SnapshotRate <= 0) {
                throw new InvalidOperationException(
                    "NetConfig rates must be positive: serverTickRate=" + ServerTickRate.ToString()
                    + ", clientSendRate=" + ClientSendRate.ToString()
                    + ", snapshotRate=" + SnapshotRate.ToString() + ".");
            }

            if (ClientSendRate == ServerTickRate) {
                return;
            }

            throw new InvalidOperationException(
                "NetConfig clientSendRate (" + ClientSendRate.ToString() + " Hz) must equal serverTickRate ("
                + ServerTickRate.ToString() + " Hz): an owning client predicts one motor step per input it sends "
                + "and the server steps one input per tick, so any other pairing predicts a different distance "
                + "than it is corrected to.");
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
