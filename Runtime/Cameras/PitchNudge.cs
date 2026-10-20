using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// An additive pitch change eased in over time (smoothstep, no overshoot), for a rig to fold into its
    /// own pitch each frame. Starting a new nudge while one runs carries the unplayed remainder into it.
    /// </summary>
    /// <remarks>
    /// Pure and time-stepped by the caller, so a rig keeps one per instance and a gate can drive it.
    /// Clamping to the rig's limits is the rig's business; this only says how far to turn this frame.
    /// </remarks>
    public class PitchNudge {
        private float _amount;
        private float _seconds;
        private float _elapsed;
        private float _given;

        /// <summary>True while some of the nudge is still to be given out.</summary>
        public bool IsRunning => _amount != _given;

        /// <summary>Degrees still to come.</summary>
        public float Remaining => _amount - _given;

        /// <summary>Starts a nudge of <paramref name="degrees"/> plus whatever was left of the last, over <paramref name="seconds"/>.</summary>
        public void Begin(float degrees, float seconds) {
            _amount = Remaining + degrees;
            _seconds = Mathf.Max(seconds, 0f);
            _elapsed = 0f;
            _given = 0f;
        }

        /// <summary>Drops whatever is left unplayed.</summary>
        public void Cancel() {
            _amount = 0f;
            _given = 0f;
        }

        /// <summary>Degrees to turn this step of <paramref name="deltaTime"/> seconds; 0 when nothing runs.</summary>
        public float Step(float deltaTime) {
            if (!IsRunning) return 0f;

            _elapsed += Mathf.Max(deltaTime, 0f);
            float progress = _seconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / _seconds));
            float wanted = _amount * progress;
            float delta = wanted - _given;
            _given = wanted;
            if (progress >= 1f) Cancel();

            return delta;
        }
    }
}
