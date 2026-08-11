using AlpineLib.DI;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Timing;
using AlpineLib.Networking;
using AlpineLib.Sessions;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Play mode inspector for <see cref="NetworkService"/>: which mode the transport is in, whether the
    /// client is connected and how far away the server is, plus the tuning both ends agreed on.
    /// </summary>
    /// <remarks>
    /// The configuration block is drawn from the live <see cref="NetConfig"/> rather than from the
    /// <c>NetworkConfig</c> asset, because they are not always the same thing: the service is handed its
    /// config by whoever composed it, and a run started from a different session config would otherwise
    /// be read through the wrong asset's numbers.
    /// </remarks>
    [CustomEditor(typeof(NetworkService))]
    public class NetworkServiceEditor : UnityEditor.Editor {
        private const string missingValue = "—";

        private bool _isConfigExpanded;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            if (!Application.isPlaying) return;

            var service = (NetworkService)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Network", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Mode", service.Mode.ToString());

            DrawClient(service.Client);
            DrawServer(service.Server);
            DrawConfig(service.Config);

            Repaint();
        }

        private static void DrawClient(NetClient client) {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Client", EditorStyles.boldLabel);

            if (client == null) {
                EditorGUILayout.LabelField("State", "Not started");
                return;
            }

            EditorGUILayout.LabelField("State", client.State.ToString());
            EditorGUILayout.LabelField("Ping", client.IsConnected ? $"{client.PingMs} ms" : missingValue);

            DrawClock(client.Clock);
        }

        /// <remarks>
        /// The estimated server tick is the number worth watching: prediction, interpolation and every
        /// correction are stamped against it, so a clock that has not synchronised explains a whole class
        /// of "the remote penguin is standing still" reports on its own.
        /// </remarks>
        private static void DrawClock(NetClock clock) {
            if (clock == null) return;

            EditorGUILayout.LabelField("Clock", clock.IsSynchronized ? "Synchronized" : "Waiting for first sync");
            EditorGUILayout.LabelField("Server Tick", clock.EstimatedServerTick.ToString());
            DrawInterpolationTimeline();
        }

        /// <remarks>
        /// The delay is no longer a configured constant on the clock — it lives on the session's
        /// <see cref="InterpolationTimeline"/> and follows latency and jitter, which is exactly why it
        /// is worth watching live here.
        /// </remarks>
        private static void DrawInterpolationTimeline() {
            InterpolationTimeline timeline = ResolveTimeline();

            if (timeline == null) {
                EditorGUILayout.LabelField("Interp Delay", missingValue);
                return;
            }

            EditorGUILayout.LabelField(
                "Interp Delay",
                $"{timeline.DelaySeconds * 1000.0:0} ms (target {timeline.TargetDelaySeconds * 1000.0:0} ms, jitter {timeline.JitterSeconds * 1000.0:0.0} ms)");
        }

        private static InterpolationTimeline ResolveTimeline() {
            if (!Injector.HasInstance) return null;
            if (!Injector.Instance.TryResolve(out ISessionService sessionService)) return null;

            return sessionService.Replication?.Timeline;
        }

        private static void DrawServer(NetServer server) {
            if (server == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", server.IsRunning ? "Running" : "Stopped");
            EditorGUILayout.LabelField("Peers", server.Peers.Count.ToString());
            EditorGUILayout.LabelField("Tick", server.Tick.ToString());
        }

        private void DrawConfig(NetConfig config) {
            EditorGUILayout.Space(4);

            _isConfigExpanded = EditorGUILayout.Foldout(_isConfigExpanded, "Live Config", true, EditorStyles.foldoutHeader);

            if (!_isConfigExpanded) return;

            if (config == null) {
                EditorGUILayout.LabelField("Config", "Not configured");
                return;
            }

            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Connect Key", config.BuildConnectKey());
            EditorGUILayout.LabelField("Port", config.Port.ToString());
            EditorGUILayout.LabelField("Max Peers", config.MaxPeers.ToString());
            EditorGUILayout.LabelField("Server Tick Rate", $"{config.ServerTickRate} Hz");
            EditorGUILayout.LabelField("Snapshot Rate", $"{config.SnapshotRate} Hz");
            EditorGUILayout.LabelField("Client Send Rate", $"{config.ClientSendRate} Hz");
            EditorGUILayout.LabelField("Movement Profiles", DescribeProfileCount(config));

            EditorGUI.indentLevel--;
        }

        private static string DescribeProfileCount(NetConfig config) {
            if (config.MovementProfiles == null) return "0";

            return config.MovementProfiles.Length.ToString();
        }
    }
}
