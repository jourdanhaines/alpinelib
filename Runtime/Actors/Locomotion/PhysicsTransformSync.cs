using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// Pushes this frame's transform writes into the physics scene, once per frame, for anything that is
    /// about to query it.
    /// </summary>
    /// <remarks>
    /// With <c>Physics.autoSyncTransforms</c> off, a collider moved in <c>Update</c> stays where the
    /// last physics step left it until the next one — up to a whole fixed step behind what is drawn,
    /// which at train speeds is most of a metre. Every sweep the motor makes, and every probe a game
    /// makes at a moving deck, has to see the colliders where the transforms are now. Guarded by frame
    /// count so any number of callers cost one sync.
    /// </remarks>
    public static class PhysicsTransformSync {
        private static int _lastSyncedFrame = -1;

        public static void EnsureSyncedThisFrame() {
            if (_lastSyncedFrame == Time.frameCount) return;

            _lastSyncedFrame = Time.frameCount;
            Physics.SyncTransforms();
        }
    }
}
