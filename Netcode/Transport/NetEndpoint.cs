using System;

namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// An address a client can connect to, independent of which transport implements it.
    /// </summary>
    /// <remarks>
    /// Join codes never encode an endpoint — they select a session once a connection exists. The
    /// endpoint always comes from configuration (or, later, a backend directory), which is why the
    /// only factories here are the configured direct address and the reserved Steam identity.
    /// </remarks>
    public readonly struct NetEndpoint : IEquatable<NetEndpoint> {
        private readonly TransportKind _kind;
        private readonly string _host;
        private readonly int _port;
        private readonly ulong _remoteId;

        private NetEndpoint(TransportKind kind, string host, int port, ulong remoteId) {
            _kind = kind;
            _host = host;
            _port = port;
            _remoteId = remoteId;
        }

        /// <summary>Which transport is being addressed.</summary>
        public TransportKind Kind => _kind;

        /// <summary>Host name or IP literal. Empty for <see cref="TransportKind.Steam"/>.</summary>
        public string Host => _host ?? string.Empty;

        /// <summary>UDP port. Zero for <see cref="TransportKind.Steam"/>.</summary>
        public int Port => _port;

        /// <summary>Steam identity. Zero for <see cref="TransportKind.Direct"/>.</summary>
        public ulong RemoteId => _remoteId;

        /// <summary>An endpoint that addresses nothing — the default value.</summary>
        public static NetEndpoint None => default;

        /// <summary>True when this endpoint carries enough information to dial.</summary>
        public bool IsValid {
            get {
                if (_kind == TransportKind.Steam) {
                    return _remoteId != 0UL;
                }

                return !string.IsNullOrEmpty(_host) && _port > 0 && _port <= 65535;
            }
        }

        /// <summary>Addresses a host and port over the direct UDP transport.</summary>
        public static NetEndpoint Direct(string host, int port) {
            return new NetEndpoint(TransportKind.Direct, host, port, 0UL);
        }

        /// <summary>Addresses a Steam identity. Reserved — no transport consumes this in v1.</summary>
        public static NetEndpoint Steam(ulong steamId) {
            return new NetEndpoint(TransportKind.Steam, string.Empty, 0, steamId);
        }

        /// <inheritdoc />
        public bool Equals(NetEndpoint other) {
            return _kind == other._kind
                && _port == other._port
                && _remoteId == other._remoteId
                && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is NetEndpoint other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            int hash = (int)_kind;
            hash = (hash * 397) ^ _port;
            hash = (hash * 397) ^ _remoteId.GetHashCode();
            hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Host);
            return hash;
        }

        /// <inheritdoc />
        public override string ToString() {
            if (_kind == TransportKind.Steam) {
                return "steam:" + _remoteId.ToString();
            }

            return Host + ":" + _port.ToString();
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(NetEndpoint left, NetEndpoint right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(NetEndpoint left, NetEndpoint right) {
            return !left.Equals(right);
        }
    }
}
