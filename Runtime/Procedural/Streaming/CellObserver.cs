using UnityEngine;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>One point cells stream around, in world space.</summary>
    public readonly struct CellObserver {
        /// <summary>An observer at <paramref name="position"/>; <paramref name="isLocal"/> marks the viewer builds are prioritised for.</summary>
        public CellObserver(ulong id, Vector3 position, bool isLocal) {
            Id = id;
            Position = position;
            IsLocal = isLocal;
        }

        /// <summary>Stable identity across frames.</summary>
        public ulong Id { get; }

        /// <summary>World position.</summary>
        public Vector3 Position { get; }

        /// <summary>True for the local viewer: pending cells build nearest-first to local observers.</summary>
        public bool IsLocal { get; }
    }
}
