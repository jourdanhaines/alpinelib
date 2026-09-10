using System.Collections.Generic;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Server.Sessions {
    /// <summary>One session as the directory and admin surfaces see it: a copy, taken between two ticks.</summary>
    public sealed class SessionSnapshot {
        public SessionSnapshot(
            string sessionId,
            string joinCode,
            SessionPhase phase,
            PlayerId ownerPlayerId,
            string matchId,
            int matchSequence,
            int connectedMemberCount,
            IReadOnlyList<SessionMemberSnapshot> members) {
            SessionId = sessionId ?? string.Empty;
            JoinCode = joinCode ?? string.Empty;
            Phase = phase;
            OwnerPlayerId = ownerPlayerId;
            MatchId = matchId ?? string.Empty;
            MatchSequence = matchSequence;
            ConnectedMemberCount = connectedMemberCount;
            Members = members;
        }

        /// <summary>Server-unique identifier of the session.</summary>
        public string SessionId { get; }

        /// <summary>The code friends type to reach it.</summary>
        public string JoinCode { get; }

        /// <summary>Where the session sits in its lifecycle.</summary>
        public SessionPhase Phase { get; }

        /// <summary>Who owns it, or <see cref="PlayerId.None"/> while ownerless.</summary>
        public PlayerId OwnerPlayerId { get; }

        /// <summary>Wire id of the match being loaded or played, or empty in lobby and results.</summary>
        public string MatchId { get; }

        /// <summary>Launch counter of the current match.</summary>
        public int MatchSequence { get; }

        /// <summary>How many members hold a live connection right now.</summary>
        public int ConnectedMemberCount { get; }

        /// <summary>The roster, reservations included, in join order.</summary>
        public IReadOnlyList<SessionMemberSnapshot> Members { get; }

        /// <summary>Roster size including seats held open for a rejoin.</summary>
        public int MemberCount => Members == null ? 0 : Members.Count;
    }
}
