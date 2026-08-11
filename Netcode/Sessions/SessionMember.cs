using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// One row of a session roster.
    /// </summary>
    /// <remarks>
    /// A member with <see cref="IsConnected"/> false is a rejoin reservation: the slot, id and name
    /// are held while the pawn is despawned, so a returning player reclaims the same identity. That is
    /// why <see cref="PeerId"/> is not the key — it is recycled by the transport and is refreshed on
    /// every reconnect.
    /// </remarks>
    public sealed class SessionMember {
        /// <summary>Peer id meaning "no live connection", used by rejoin reservations.</summary>
        public const int NoPeerId = -1;

        /// <summary>Creates an empty member, ready to be deserialised into.</summary>
        public SessionMember() {
            PeerId = NoPeerId;
            PlayerId = PlayerId.None;
            DisplayName = string.Empty;
        }

        /// <summary>Creates a connected member.</summary>
        public SessionMember(int peerId, PlayerId playerId, string displayName, bool isOwner, byte partyId) {
            PeerId = peerId;
            PlayerId = playerId;
            DisplayName = PlayerIdentity.Sanitize(displayName);
            IsOwner = isOwner;
            IsConnected = true;
            PartyId = partyId;
        }

        /// <summary>Transport slot of the live connection, or <see cref="NoPeerId"/> when disconnected.</summary>
        public int PeerId { get; set; }

        /// <summary>Stable identity this roster slot is reserved for.</summary>
        public PlayerId PlayerId { get; set; }

        /// <summary>Name shown to other members.</summary>
        public string DisplayName { get; set; }

        /// <summary>True for the member who may launch matches and kick, per the lobby config.</summary>
        public bool IsOwner { get; set; }

        /// <summary>False while a rejoin reservation is being held open.</summary>
        public bool IsConnected { get; set; }

        /// <summary>Party grouping. v1 hardcodes every lobby member into one party; this is the seam.</summary>
        public byte PartyId { get; set; }

        /// <summary>Game-defined appearance code the member joined with. Zero when unset.</summary>
        /// <remarks>
        /// Carried on the roster rather than on spawn messages because it is a property of the player,
        /// not of any one pawn — and the roster reaches every client before their pawn does.
        /// </remarks>
        public ushort AvatarData { get; set; }

        /// <summary>Writes the member to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteInt(PeerId);
            PlayerId.Serialize(ref writer);
            writer.WriteString(DisplayName ?? string.Empty);
            writer.WriteByte(PackFlags());
            writer.WriteByte(PartyId);
            writer.WriteUShort(AvatarData);
        }

        /// <summary>Reads a member written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            PeerId = reader.ReadInt();
            PlayerId = PlayerId.Deserialize(ref reader);
            DisplayName = PlayerIdentity.Sanitize(reader.ReadString());
            UnpackFlags(reader.ReadByte());
            PartyId = reader.ReadByte();
            AvatarData = reader.ReadUShort();
        }

        private byte PackFlags() {
            byte flags = 0;

            if (IsOwner) {
                flags |= 1 << 0;
            }

            if (IsConnected) {
                flags |= 1 << 1;
            }

            return flags;
        }

        private void UnpackFlags(byte flags) {
            IsOwner = (flags & (1 << 0)) != 0;
            IsConnected = (flags & (1 << 1)) != 0;
        }

        /// <inheritdoc />
        public override string ToString() {
            return DisplayName + " [peer " + PeerId.ToString() + (IsConnected ? "]" : ", disconnected]");
        }
    }
}
