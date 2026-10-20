using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace AlpineLib.Input {
    /// <summary>
    /// Tells a look reader when to drop the pointer delta because the cursor lock just changed.
    /// </summary>
    /// <remarks>
    /// Locking or unlocking the cursor warps it (to the window centre, or back to where it was), and
    /// the platform reports that warp as one large mouse delta on the frame of the change or the next.
    /// Dropping both frames removes the camera snap a menu opening or closing would otherwise cause.
    ///
    /// In play mode the lock state is sampled every frame by a player-loop step at the start of the
    /// frame, so a change is seen even on frames nobody reads look. Before any sample exists, or when
    /// the step is not running (edit mode, a replaced player loop), the state is sampled on read and a
    /// change drops the next two reads instead, since the frame count does not advance there. The very
    /// first sample counts as a change.
    /// </remarks>
    public static class PointerLookGuard {
        private const int DroppedFrames = 2;

        private static int _loopSampledFrame = -1;
        private static int _changedFrame = int.MinValue;
        private static int _dropsRemaining;
        private static CursorLockMode _lastLockState;
        private static bool _hasSample;

        /// <summary>True on the frame the cursor lock state changed and the frame after.</summary>
        public static bool ShouldDrop() {
            if (Application.isPlaying && _loopSampledFrame == Time.frameCount) {
                return Time.frameCount - _changedFrame < DroppedFrames;
            }

            if (Sample()) _dropsRemaining = DroppedFrames;
            if (_dropsRemaining <= 0) return false;

            _dropsRemaining--;
            return true;
        }

        /// <summary>Records the current lock state; true when it differs from the last one (or is the first).</summary>
        private static bool Sample() {
            CursorLockMode lockState = Cursor.lockState;
            bool changed = !_hasSample || lockState != _lastLockState;
            _lastLockState = lockState;
            _hasSample = true;
            return changed;
        }

        private static void SampleFromLoop() {
            if (!Application.isPlaying) return;

            int frame = Time.frameCount;
            _loopSampledFrame = frame;
            if (Sample()) _changedFrame = frame;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install() {
            _loopSampledFrame = -1;
            _changedFrame = int.MinValue;
            _dropsRemaining = 0;
            _hasSample = false;

            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (TryAddToPhase(ref loop, typeof(EarlyUpdate))) PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool TryAddToPhase(ref PlayerLoopSystem loop, Type phase) {
            if (loop.subSystemList == null) return false;

            for (int index = 0; index < loop.subSystemList.Length; index++) {
                if (loop.subSystemList[index].type != phase) continue;

                return TryAppendStep(ref loop.subSystemList[index]);
            }

            return false;
        }

        /// <summary>Appends the sampling step to a phase once; a reinstall after a domain-reload-free play is a no-op.</summary>
        private static bool TryAppendStep(ref PlayerLoopSystem phase) {
            PlayerLoopSystem[] steps = phase.subSystemList ?? new PlayerLoopSystem[0];
            foreach (PlayerLoopSystem step in steps) {
                if (step.type == typeof(PointerLookGuard)) return false;
            }

            var extended = new PlayerLoopSystem[steps.Length + 1];
            Array.Copy(steps, extended, steps.Length);
            extended[steps.Length] = new PlayerLoopSystem {
                type = typeof(PointerLookGuard),
                updateDelegate = SampleFromLoop
            };
            phase.subSystemList = extended;
            return true;
        }
    }
}
