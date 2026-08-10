using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Engine-free mirror of the <c>LobbyConfig</c> authoring asset: the igloo players return to
    /// between matches.
    /// </summary>
    public sealed class LobbyConfigData {
        /// <summary>Creates a lobby config carrying the shipped defaults.</summary>
        public LobbyConfigData() {
            DisplayName = string.Empty;
            LobbySceneName = string.Empty;
            LobbyCapacity = 8;
            OwnerCanKick = true;
            OwnerLaunchesMatches = true;
        }

        /// <summary>Human-readable lobby name, shown in menus and the session directory.</summary>
        public string DisplayName { get; set; }

        /// <summary>Scene loaded for the lobby phase.</summary>
        public string LobbySceneName { get; set; }

        /// <summary>Members allowed in the lobby. Never larger than the profile's player cap.</summary>
        public int LobbyCapacity { get; set; }

        /// <summary>Whether the session owner may kick members.</summary>
        public bool OwnerCanKick { get; set; }

        /// <summary>Whether only the owner may launch matches.</summary>
        public bool OwnerLaunchesMatches { get; set; }

        /// <summary>Writes the lobby config to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteString(DisplayName ?? string.Empty);
            writer.WriteString(LobbySceneName ?? string.Empty);
            writer.WriteInt(LobbyCapacity);
            writer.WriteByte(PackFlags());
        }

        /// <summary>Reads a lobby config written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            DisplayName = reader.ReadString();
            LobbySceneName = reader.ReadString();
            LobbyCapacity = reader.ReadInt();
            UnpackFlags(reader.ReadByte());
        }

        private byte PackFlags() {
            byte flags = 0;

            if (OwnerCanKick) {
                flags |= 1 << 0;
            }

            if (OwnerLaunchesMatches) {
                flags |= 1 << 1;
            }

            return flags;
        }

        private void UnpackFlags(byte flags) {
            OwnerCanKick = (flags & (1 << 0)) != 0;
            OwnerLaunchesMatches = (flags & (1 << 1)) != 0;
        }
    }
}
