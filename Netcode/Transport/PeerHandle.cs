using System;

namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// Opaque identifier for a connected peer, handed out by the transport and used everywhere above it
    /// as the addressee of a send. It is deliberately a bare integer rather than a reference to a
    /// transport peer object: the session, replication and chat layers must be able to hold, compare and
    /// store a peer identity without taking a dependency on any particular transport implementation.
    ///
    /// Handles are only meaningful to the transport instance that issued them, and are not stable across
    /// a reconnect — player identity lives in <c>PlayerId</c>, not here.
    /// </summary>
    public readonly struct PeerHandle : IEquatable<PeerHandle> {
        /// <summary>The absence of a peer: what a client sees as the sender of a server message.</summary>
        public static readonly PeerHandle None = new PeerHandle(-1);

        private readonly int id;

        public PeerHandle(int id) {
            this.id = id;
        }

        /// <summary>Transport-assigned peer index.</summary>
        public int Id => id;

        /// <summary>False for <see cref="None"/> and any other negative handle.</summary>
        public bool IsValid => id >= 0;

        public bool Equals(PeerHandle other) {
            return id == other.id;
        }

        public override bool Equals(object obj) {
            return obj is PeerHandle other && Equals(other);
        }

        public override int GetHashCode() {
            return id;
        }

        public override string ToString() {
            return IsValid ? "Peer(" + id + ")" : "Peer(None)";
        }

        public static bool operator ==(PeerHandle left, PeerHandle right) {
            return left.Equals(right);
        }

        public static bool operator !=(PeerHandle left, PeerHandle right) {
            return !left.Equals(right);
        }
    }
}
