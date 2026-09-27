using UnityEngine;

namespace AlpineLib.Origin {
    /// <summary>
    /// Something holding world-frame positions outside the transform hierarchy (motor state, velocity
    /// estimators, camera smoothing) that must move with a floating-origin rebase.
    /// </summary>
    public interface IOriginShiftListener {
        /// <summary>Every world-frame position gained <paramref name="delta"/>; add it to whatever is cached.</summary>
        void OnOriginShifted(Vector3 delta);
    }
}
