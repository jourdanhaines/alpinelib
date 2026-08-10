using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The whole visible state of a session at one instant: who is in it, what phase it is in, and how
    /// to invite someone else.
    /// </summary>
    /// <remarks>
    /// Sent once inside <c>JoinAccepted</c> so an arriving or rejoining client starts from an
    /// authoritative roster rather than replaying the incremental member messages it missed. Rejoin
    /// reservations are included, so members with <see cref="SessionMember.IsConnected"/> false appear
    /// here too.
    /// </remarks>
    public sealed class LobbySnapshot {
        private const int MaxMemberCount = 256;

        /// <summary>Creates an empty snapshot, ready to be deserialised into.</summary>
        public LobbySnapshot() {
            SessionId = string.Empty;
            JoinCode = string.Empty;
            Phase = SessionPhase.Lobby;
            Members = new List<SessionMember>();
        }

        /// <summary>Server-assigned session identity, unique within the process.</summary>
        public string SessionId { get; set; }

        /// <summary>The code friends type to join. Empty when join codes are disabled.</summary>
        public string JoinCode { get; set; }

        /// <summary>Phase the session is in right now.</summary>
        public SessionPhase Phase { get; set; }

        /// <summary>Full roster, rejoin reservations included.</summary>
        public List<SessionMember> Members { get; set; }

        /// <summary>Finds a roster row by identity, or null when the player is not a member.</summary>
        public SessionMember FindMember(PlayerId playerId) {
            if (Members == null) {
                return null;
            }

            for (int memberIndex = 0; memberIndex < Members.Count; memberIndex++) {
                SessionMember candidate = Members[memberIndex];

                if (candidate != null && candidate.PlayerId == playerId) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Writes the snapshot to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteString(SessionId ?? string.Empty);
            writer.WriteString(JoinCode ?? string.Empty);
            writer.WriteByte((byte)Phase);

            int memberCount = Members == null ? 0 : Members.Count;
            writer.WriteUShort((ushort)memberCount);

            for (int memberIndex = 0; memberIndex < memberCount; memberIndex++) {
                (Members[memberIndex] ?? new SessionMember()).Serialize(ref writer);
            }
        }

        /// <summary>Reads a snapshot written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            SessionId = reader.ReadString();
            JoinCode = reader.ReadString();
            Phase = (SessionPhase)reader.ReadByte();

            int memberCount = reader.ReadUShort();

            if (memberCount > MaxMemberCount) {
                throw new NetProtocolException("LobbySnapshot declared " + memberCount.ToString()
                    + " members, which exceeds the sanity cap of " + MaxMemberCount.ToString() + ".");
            }

            Members = new List<SessionMember>(memberCount);

            for (int memberIndex = 0; memberIndex < memberCount; memberIndex++) {
                SessionMember member = new SessionMember();
                member.Deserialize(ref reader);
                Members.Add(member);
            }
        }
    }
}
