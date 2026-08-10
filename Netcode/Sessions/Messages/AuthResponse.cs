using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The validator's verdict on an <see cref="AuthRequest"/>.
    /// </summary>
    /// <remarks>
    /// Acceptance moves the connection to the server front desk — authenticated, attached to no
    /// session — so this message does not carry a roster or a config; that arrives later with
    /// <see cref="JoinAccepted"/>. <see cref="PeerId"/> is the transport slot the server will address
    /// this connection by, echoed back so the client can recognise itself in roster rows.
    /// </remarks>
    public struct AuthResponse : INetMessage {
        /// <summary>Longest rejection reason accepted on the wire.</summary>
        public const int MaxReasonLength = 256;

        /// <summary>Creates an acceptance carrying the assigned peer id.</summary>
        public static AuthResponse Accept(int peerId) {
            return new AuthResponse { Accepted = true, PeerId = peerId, Reason = string.Empty };
        }

        /// <summary>Creates a rejection carrying a human-readable reason.</summary>
        public static AuthResponse Reject(string reason) {
            return new AuthResponse { Accepted = false, PeerId = SessionMember.NoPeerId, Reason = reason ?? string.Empty };
        }

        /// <summary>True when the connection may proceed to session attach.</summary>
        public bool Accepted { get; set; }

        /// <summary>Transport slot assigned to this connection, or -1 when rejected.</summary>
        public int PeerId { get; set; }

        /// <summary>Rejection reason for display. Empty when accepted.</summary>
        public string Reason { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string reason = Reason ?? string.Empty;

            if (reason.Length > MaxReasonLength) {
                reason = reason.Substring(0, MaxReasonLength);
            }

            writer.WriteBool(Accepted);
            writer.WriteInt(PeerId);
            writer.WriteString(reason);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Accepted = reader.ReadBool();
            PeerId = reader.ReadInt();
            Reason = reader.ReadString();

            if (Reason.Length > MaxReasonLength) {
                throw new NetProtocolException("AuthResponse declared a reason of " + Reason.Length.ToString()
                    + " characters, which exceeds the cap of " + MaxReasonLength.ToString() + ".");
            }
        }
    }
}
