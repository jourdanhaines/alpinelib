using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>Where a marker is drawn: its viewport position, whether it was pulled in to the edge, and the bearing from the viewport centre to its target.</summary>
    public readonly struct MapMarkerPlacement {
        /// <summary>Builds a placement.</summary>
        public MapMarkerPlacement(Vector2 position, bool isClamped, Vector2 direction) {
            Position = position;
            IsClamped = isClamped;
            Direction = direction;
        }

        /// <summary>Viewport position (canvas units from the centre).</summary>
        public Vector2 Position { get; }

        /// <summary>True when the target lies beyond the edge and the marker sits on it.</summary>
        public bool IsClamped { get; }

        /// <summary>Unit direction from the viewport centre toward the target (+y up when the target is at the centre).</summary>
        public Vector2 Direction { get; }

        /// <summary>Bearing of <see cref="Direction"/>, degrees clockwise from viewport up.</summary>
        public float BearingDegrees => MapMarkerMath.BearingDegrees(Direction);
    }
}
