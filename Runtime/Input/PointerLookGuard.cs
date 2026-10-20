using UnityEngine;

namespace AlpineLib.Input {
    /// <summary>
    /// Tells a look reader when to drop the pointer delta because the cursor lock just changed.
    /// </summary>
    /// <remarks>
    /// Locking or unlocking the cursor warps it (to the window centre, or back to where it was), and
    /// the platform reports that warp as one large mouse delta on the frame of the change or the next.
    /// Dropping both frames removes the camera snap a menu opening or closing would otherwise cause.
    /// The lock state is sampled on the first call each frame, so call it every frame you read look.
    /// </remarks>
    public static class PointerLookGuard {
        private static int _sampledFrame = -1;
        private static int _changedFrame = int.MinValue;
        private static CursorLockMode _lastLockState;
        private static bool _hasSample;

        /// <summary>True on the frame the cursor lock state changed and the frame after.</summary>
        public static bool ShouldDrop() {
            int frame = Time.frameCount;
            if (frame != _sampledFrame) Sample(frame);

            return _changedFrame != int.MinValue && frame - _changedFrame <= 1;
        }

        private static void Sample(int frame) {
            _sampledFrame = frame;
            CursorLockMode lockState = Cursor.lockState;
            if (_hasSample && lockState != _lastLockState) _changedFrame = frame;

            _lastLockState = lockState;
            _hasSample = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() {
            _sampledFrame = -1;
            _changedFrame = int.MinValue;
            _hasSample = false;
        }
    }
}
