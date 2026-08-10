using AlpineLib.Netcode.Sessions;
using AlpineLib.Networking;
using AlpineLib.Sessions;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Play mode inspector for <see cref="SessionService"/>: the session's state and phase, the join code
    /// to read out to a friend, the round trip to the server and a card per member of the lobby.
    /// </summary>
    /// <remarks>
    /// Everything here is read off the live service rather than out of serialized fields, because none
    /// of it is serialized — a session lives entirely in memory, and until now the only way to see who
    /// the server thinks is in the room was a breakpoint. Ping is pulled from the
    /// <see cref="NetworkService"/> beside this one on the app root: it belongs to the transport, but a
    /// session read-out with no latency in it answers half a question.
    /// </remarks>
    [CustomEditor(typeof(SessionService))]
    public class SessionServiceEditor : UnityEditor.Editor {
        private const string missingValue = "—";

        private bool _isMembersExpanded = true;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            if (!Application.isPlaying) return;

            var service = (SessionService)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Session", EditorStyles.boldLabel);

            DrawConnection(service);
            DrawJoinCode(service);
            DrawMatch(service);
            DrawMembers(service);

            Repaint();
        }

        private void DrawConnection(SessionService service) {
            EditorGUILayout.LabelField("Mode", ResolveMode(service));
            EditorGUILayout.LabelField("State", service.State.ToString());
            EditorGUILayout.LabelField("Phase", service.Phase.ToString());
            EditorGUILayout.LabelField("Ping", ResolvePing(service));
            EditorGUILayout.LabelField("Session Id", Displayed(service.SessionId));
            EditorGUILayout.LabelField("Local Player", DescribeIdentity(service.Identity));
            EditorGUILayout.LabelField("Owner", service.IsOwner ? "This client" : "Another member");
        }

        /// <summary>
        /// Draws the join code with a copy button, since reading six characters out of an inspector and
        /// typing them into a second editor is how this gets tested.
        /// </summary>
        private void DrawJoinCode(SessionService service) {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Join Code", Displayed(service.CurrentJoinCode));

            if (GUILayout.Button("Copy", GUILayout.Width(48))) {
                EditorGUIUtility.systemCopyBuffer = service.CurrentJoinCode ?? string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawMatch(SessionService service) {
            MatchContextData match = service.CurrentMatch;
            if (match == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Match", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Match Id", Displayed(match.MatchId));
            EditorGUILayout.LabelField("Scene", Displayed(match.SceneName));
            EditorGUILayout.LabelField("Run", match.MatchSequence.ToString());
        }

        private void DrawMembers(SessionService service) {
            var members = service.Members;
            int memberCount = members?.Count ?? 0;

            EditorGUILayout.Space(4);
            _isMembersExpanded = EditorGUILayout.Foldout(
                _isMembersExpanded, $"Members ({memberCount})", true, EditorStyles.foldoutHeader);

            if (!_isMembersExpanded || memberCount == 0) return;

            EditorGUI.indentLevel++;

            foreach (SessionMember member in members) {
                DrawMember(member, service);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawMember(SessionMember member, SessionService service) {
            if (member == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(DescribeMemberTitle(member, service), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Player Id", member.PlayerId.ToString());
            EditorGUILayout.LabelField("Peer", DescribePeer(member));
            EditorGUILayout.LabelField("Connection", member.IsConnected ? "Connected" : "Disconnected (rejoin reserved)");
            EditorGUILayout.LabelField("Party", member.PartyId.ToString());

            EditorGUILayout.EndVertical();
        }

        private static string DescribeMemberTitle(SessionMember member, SessionService service) {
            string title = string.IsNullOrEmpty(member.DisplayName) ? "(unnamed)" : member.DisplayName;

            if (member.IsOwner) title += " — owner";
            if (IsLocalMember(member, service)) title += " — you";

            return title;
        }

        private static bool IsLocalMember(SessionMember member, SessionService service) {
            if (service.Identity == null) return false;

            return member.PlayerId == service.Identity.PlayerId;
        }

        private static string DescribePeer(SessionMember member) {
            if (member.PeerId == SessionMember.NoPeerId) return missingValue;

            return member.PeerId.ToString();
        }

        /// <summary>
        /// Names what this process is doing on the network: the transport's mode, plus the fact that a
        /// server for this session is running here, which the mode alone does not say on a client that
        /// dialled its own loopback.
        /// </summary>
        private static string ResolveMode(SessionService service) {
            NetworkService networkService = ResolveNetworkService(service);
            string mode = networkService != null ? networkService.Mode.ToString() : "No NetworkService";

            if (!service.IsListenHosting) return mode;

            return $"{mode} (listen hosting)";
        }

        private static string ResolvePing(SessionService service) {
            NetworkService networkService = ResolveNetworkService(service);
            if (networkService == null) return missingValue;
            if (networkService.Client == null || !networkService.Client.IsConnected) return missingValue;

            return $"{networkService.Client.PingMs} ms";
        }

        private static NetworkService ResolveNetworkService(SessionService service) {
            return service.GetComponent<NetworkService>();
        }

        private static string DescribeIdentity(PlayerIdentity identity) {
            if (identity == null) return missingValue;

            return $"{identity.DisplayName} ({identity.PlayerId})";
        }

        private static string Displayed(string value) {
            return string.IsNullOrEmpty(value) ? missingValue : value;
        }
    }
}
