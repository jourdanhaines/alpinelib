using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// A player's stable identity across connections. Survives a transport drop, which is what makes
    /// rejoin possible: the roster slot is reserved against this value, not against a peer handle.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>PeerHandle</c>, which is a per-connection transport slot and is recycled.
    /// Persisted client-side by the identity store and echoed back in <c>AuthRequest</c>.
    /// </remarks>
    public readonly struct PlayerId : IEquatable<PlayerId> {
        /// <summary>Wire width of a serialised id.</summary>
        public const int SerializedByteCount = 16;

        private readonly Guid _value;

        /// <summary>Wraps an existing GUID.</summary>
        public PlayerId(Guid value) {
            _value = value;
        }

        /// <summary>The underlying GUID.</summary>
        public Guid Value => _value;

        /// <summary>False for the default value, which never identifies a real player.</summary>
        public bool IsValid => _value != Guid.Empty;

        /// <summary>The id that identifies nobody.</summary>
        public static PlayerId None => default;

        /// <summary>Mints a fresh identity.</summary>
        public static PlayerId NewId() {
            return new PlayerId(Guid.NewGuid());
        }

        /// <summary>Parses the compact form produced by <see cref="ToString"/>.</summary>
        public static bool TryParse(string text, out PlayerId playerId) {
            if (string.IsNullOrWhiteSpace(text) || !Guid.TryParse(text, out Guid parsed)) {
                playerId = None;
                return false;
            }

            playerId = new PlayerId(parsed);
            return true;
        }

        /// <summary>Writes the id as 16 raw bytes.</summary>
        public void Serialize(ref NetWriter writer) {
            byte[] bytes = _value.ToByteArray();

            for (int byteIndex = 0; byteIndex < SerializedByteCount; byteIndex++) {
                writer.WriteByte(bytes[byteIndex]);
            }
        }

        /// <summary>Reads an id written by <see cref="Serialize"/>.</summary>
        public static PlayerId Deserialize(ref NetReader reader) {
            byte[] bytes = new byte[SerializedByteCount];

            for (int byteIndex = 0; byteIndex < SerializedByteCount; byteIndex++) {
                bytes[byteIndex] = reader.ReadByte();
            }

            return new PlayerId(new Guid(bytes));
        }

        /// <inheritdoc />
        public bool Equals(PlayerId other) {
            return _value.Equals(other._value);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is PlayerId other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            return _value.GetHashCode();
        }

        /// <summary>Compact 32-character hexadecimal form, suitable for logs and the identity file.</summary>
        public override string ToString() {
            return _value.ToString("N");
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(PlayerId left, PlayerId right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(PlayerId left, PlayerId right) {
            return !left.Equals(right);
        }
    }
}
