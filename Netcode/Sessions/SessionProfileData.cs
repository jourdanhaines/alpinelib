using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Engine-free mirror of the <c>SessionProfile</c> authoring asset: the rules a session runs by.
    /// </summary>
    /// <remarks>
    /// Travels to clients inside <c>JoinAccepted</c> as binary — deliberately no JSON here, because
    /// these sources compile into Unity as well. The .NET server reads the editor-exported JSON with
    /// System.Text.Json in net10-only code and populates this object.
    /// </remarks>
    public sealed class SessionProfileData {
        /// <summary>Creates a profile carrying the shipped defaults.</summary>
        public SessionProfileData() {
            ProfileId = string.Empty;
            LifetimeMode = SessionLifetimeMode.LobbyScoped;
            HostPolicy = HostPolicy.TransferToMember;
            RejoinPolicy = RejoinPolicy.AnyTime;
            RejoinWindowSeconds = 120f;
            MaxPlayers = 8;
            ReadyTimeoutSeconds = 30f;
            LateLoadPolicy = LateLoadPolicy.DropToLobby;
            AllowJoinDuringMatch = false;
            ResultsHoldSeconds = 8f;
            EmptyShutdownSeconds = 300f;
        }

        /// <summary>Identifies which profile a session was created from.</summary>
        public string ProfileId { get; set; }

        /// <summary>When the session tears itself down.</summary>
        public SessionLifetimeMode LifetimeMode { get; set; }

        /// <summary>What happens when the owner leaves.</summary>
        public HostPolicy HostPolicy { get; set; }

        /// <summary>Whether a dropped member may reclaim their slot.</summary>
        public RejoinPolicy RejoinPolicy { get; set; }

        /// <summary>How long a slot is held under <see cref="RejoinPolicy.TimedWindow"/>. Ignored otherwise.</summary>
        public float RejoinWindowSeconds { get; set; }

        /// <summary>Hard cap on roster size, reservations included.</summary>
        public int MaxPlayers { get; set; }

        /// <summary>How long the match ready barrier waits before applying the late-load policy.</summary>
        public float ReadyTimeoutSeconds { get; set; }

        /// <summary>What to do with members who miss the ready barrier.</summary>
        public LateLoadPolicy LateLoadPolicy { get; set; }

        /// <summary>Reserved; keep false. Mid-match joins arrive through rejoin, not fresh joins.</summary>
        public bool AllowJoinDuringMatch { get; set; }

        /// <summary>How long results are held before the session returns to the lobby.</summary>
        public float ResultsHoldSeconds { get; set; }

        /// <summary>How long an empty session survives before closing. Ignored when long-lived.</summary>
        public float EmptyShutdownSeconds { get; set; }

        /// <summary>Writes the profile to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteString(ProfileId ?? string.Empty);
            writer.WriteByte((byte)LifetimeMode);
            writer.WriteByte((byte)HostPolicy);
            writer.WriteByte((byte)RejoinPolicy);
            writer.WriteFloat(RejoinWindowSeconds);
            writer.WriteInt(MaxPlayers);
            writer.WriteFloat(ReadyTimeoutSeconds);
            writer.WriteByte((byte)LateLoadPolicy);
            writer.WriteBool(AllowJoinDuringMatch);
            writer.WriteFloat(ResultsHoldSeconds);
            writer.WriteFloat(EmptyShutdownSeconds);
        }

        /// <summary>Reads a profile written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            ProfileId = reader.ReadString();
            LifetimeMode = (SessionLifetimeMode)reader.ReadByte();
            HostPolicy = (HostPolicy)reader.ReadByte();
            RejoinPolicy = (RejoinPolicy)reader.ReadByte();
            RejoinWindowSeconds = reader.ReadFloat();
            MaxPlayers = reader.ReadInt();
            ReadyTimeoutSeconds = reader.ReadFloat();
            LateLoadPolicy = (LateLoadPolicy)reader.ReadByte();
            AllowJoinDuringMatch = reader.ReadBool();
            ResultsHoldSeconds = reader.ReadFloat();
            EmptyShutdownSeconds = reader.ReadFloat();
        }

        /// <summary>True when a dropped member's slot should be held open at all.</summary>
        public bool AllowsRejoin() {
            return RejoinPolicy != RejoinPolicy.None;
        }

        /// <summary>
        /// Seconds a reservation survives, or <see cref="float.PositiveInfinity"/> when unlimited.
        /// </summary>
        public float ResolveRejoinWindowSeconds() {
            if (RejoinPolicy == RejoinPolicy.None) {
                return 0f;
            }

            if (RejoinPolicy == RejoinPolicy.AnyTime) {
                return float.PositiveInfinity;
            }

            return RejoinWindowSeconds;
        }
    }
}
