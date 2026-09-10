using System;
using System.Collections.Generic;

namespace AlpineLib.Server.Sessions {
    /// <summary>
    /// Everything the process is hosting, as of one instant on the game thread.
    /// </summary>
    /// <remarks>
    /// The whole directory is copied in one go rather than queried session by session, because a caller
    /// that asked twice would be shown two different moments and could quite reasonably conclude that a
    /// session had appeared and vanished. One snapshot, one instant, no such story.
    /// </remarks>
    public sealed class DirectorySnapshot {
        public DirectorySnapshot(
            long capturedAtUnixMs,
            uint serverTick,
            int maxSessions,
            int connectedPeerCount,
            IReadOnlyList<SessionSnapshot> sessions) {
            CapturedAtUnixMs = capturedAtUnixMs;
            ServerTick = serverTick;
            MaxSessions = maxSessions;
            ConnectedPeerCount = connectedPeerCount;
            Sessions = sessions ?? Array.Empty<SessionSnapshot>();
        }

        /// <summary>When the copy was taken, on the server's wall clock.</summary>
        public long CapturedAtUnixMs { get; }

        /// <summary>The authoritative tick at that instant.</summary>
        public uint ServerTick { get; }

        /// <summary>How many sessions this process will host at once.</summary>
        public int MaxSessions { get; }

        /// <summary>Connections the socket is holding, attached to a session or not.</summary>
        public int ConnectedPeerCount { get; }

        /// <summary>Every live session, in creation order.</summary>
        public IReadOnlyList<SessionSnapshot> Sessions { get; }

        /// <summary>The session with this id, or null when the directory has none.</summary>
        public SessionSnapshot FindBySessionId(string sessionId) {
            if (string.IsNullOrEmpty(sessionId)) {
                return null;
            }

            for (int sessionIndex = 0; sessionIndex < Sessions.Count; sessionIndex++) {
                SessionSnapshot candidate = Sessions[sessionIndex];

                if (string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal)) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The session a join code selects, matched case-insensitively, or null.</summary>
        public SessionSnapshot FindByJoinCode(string joinCode) {
            if (string.IsNullOrEmpty(joinCode)) {
                return null;
            }

            for (int sessionIndex = 0; sessionIndex < Sessions.Count; sessionIndex++) {
                SessionSnapshot candidate = Sessions[sessionIndex];

                if (string.Equals(candidate.JoinCode, joinCode, StringComparison.OrdinalIgnoreCase)) {
                    return candidate;
                }
            }

            return null;
        }
    }
}
