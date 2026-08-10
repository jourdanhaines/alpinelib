using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Everything a client needs to load into a match: which match, which scene, who is playing, and
    /// which run of it this is.
    /// </summary>
    /// <remarks>
    /// Sent with <c>MatchLoad</c>, and again inside <c>JoinAccepted</c> when a rejoining client
    /// arrives mid-match — the rejoin path needs the same context the original participants got.
    /// <see cref="MatchSequence"/> disambiguates back-to-back runs so a late <c>ClientReady</c> from
    /// the previous match cannot satisfy the current ready barrier.
    /// </remarks>
    public sealed class MatchContextData {
        private const int MaxParticipantCount = 256;

        /// <summary>Creates an empty context, ready to be deserialised into.</summary>
        public MatchContextData() {
            MatchId = string.Empty;
            SceneName = string.Empty;
            Participants = new List<SessionMember>();
        }

        /// <summary>Wire id of the match being played.</summary>
        public string MatchId { get; set; }

        /// <summary>Scene every participant loads.</summary>
        public string SceneName { get; set; }

        /// <summary>Members taking part. v1 party rules put every lobby member here.</summary>
        public List<SessionMember> Participants { get; set; }

        /// <summary>Monotonic counter of matches launched by this session, starting at one.</summary>
        public int MatchSequence { get; set; }

        /// <summary>True when the given player is a participant in this match.</summary>
        public bool HasParticipant(PlayerId playerId) {
            if (Participants == null) {
                return false;
            }

            for (int participantIndex = 0; participantIndex < Participants.Count; participantIndex++) {
                SessionMember candidate = Participants[participantIndex];

                if (candidate != null && candidate.PlayerId == playerId) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Writes the context to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteString(MatchId ?? string.Empty);
            writer.WriteString(SceneName ?? string.Empty);
            writer.WriteInt(MatchSequence);

            int participantCount = Participants == null ? 0 : Participants.Count;
            writer.WriteUShort((ushort)participantCount);

            for (int participantIndex = 0; participantIndex < participantCount; participantIndex++) {
                (Participants[participantIndex] ?? new SessionMember()).Serialize(ref writer);
            }
        }

        /// <summary>Reads a context written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            MatchId = reader.ReadString();
            SceneName = reader.ReadString();
            MatchSequence = reader.ReadInt();

            int participantCount = reader.ReadUShort();

            if (participantCount > MaxParticipantCount) {
                throw new NetProtocolException("MatchContextData declared " + participantCount.ToString()
                    + " participants, which exceeds the sanity cap of " + MaxParticipantCount.ToString() + ".");
            }

            Participants = new List<SessionMember>(participantCount);

            for (int participantIndex = 0; participantIndex < participantCount; participantIndex++) {
                SessionMember participant = new SessionMember();
                participant.Deserialize(ref reader);
                Participants.Add(participant);
            }
        }
    }
}
