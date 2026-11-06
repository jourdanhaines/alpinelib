using System;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// Where the eyes look, from what the body does: they lead into a turn and ease back once it stops, follow the
    /// head's pitch up and down, glance the way the body strafes, and dart from side to side while it backs up. The
    /// sources add into one gaze, clamped to the square |x|, |y| ≤ 1; with nothing going on it is exactly (0, 0).
    /// </summary>
    /// <remarks>
    /// Every source has a dead zone and an ease-out curve that tops out below full, so small motion reads as nothing
    /// and large motion reads without pinning. The turn rate is low-passed first, over a fixed time, so a jittery or
    /// coarsely polled turn leads by its average speed whatever the frame rate; the lead then eases back on one
    /// smooth curve (<see cref="FaceLeadFollower"/>) that is the same at any frame rate. A held sheet's downward glance
    /// (<see cref="Step"/>'s glance) is subtracted from the vertical and damps the pitch to
    /// <see cref="FaceExpressionTuning.glancePitchKeep"/> of itself.
    /// </remarks>
    public sealed class FaceGaze {
        // Below this share of straight back the walk counts as sideways, and the darting follows the side walked.
        private const float BackwardFrom = 0.3f;
        private const float BackwardFull = 0.9f;
        private const float SidedScan = 0.3f;

        private readonly FaceLeadFollower _turn = new FaceLeadFollower();
        private readonly FaceSpring _pitch = new FaceSpring();
        private readonly FaceSpring _move = new FaceSpring();
        private readonly FaceFollower _backward = new FaceFollower();
        private readonly FaceFollower _directRight = new FaceFollower();
        private readonly FaceFollower _directUp = new FaceFollower();
        private float _turnRate;
        private float _scanElapsed;
        private float _scanSide = 1f;

        /// <summary>Gaze across after the last step, -1 full left to 1 full right (the body's own left and right).</summary>
        public float Right { get; private set; }

        /// <summary>Gaze up and down after the last step, -1 full down to 1 full up.</summary>
        public float Up { get; private set; }

        /// <summary>The turn rate the lead is shaped from, low-passed, degrees per second.</summary>
        public float SmoothedTurnRate => _turnRate;

        /// <summary>The turn's share of <see cref="Right"/>, before the clamp.</summary>
        public float TurnLook => _turn.Value;

        /// <summary>The pitch's share of <see cref="Up"/>, before the glance and the clamp.</summary>
        public float PitchLook => _pitch.Value;

        /// <summary>The strafe and back-up darting's share of <see cref="Right"/>, before the clamp.</summary>
        public float MoveLook => _move.Value;

        /// <summary>How much the body is backing up, 0 to 1, eased; what the worried and wide bias scale by.</summary>
        public float Backward => _backward.Value;

        /// <summary>
        /// Advances by <paramref name="deltaTime"/> seconds under <paramref name="signals"/>, with
        /// <paramref name="glanceDown"/> (0..1 of the eyes' range) drawn down on top.
        /// </summary>
        public void Step(in FaceSignalFrame signals, float glanceDown, float deltaTime, FaceExpressionTuning tuning) {
            float step = FaceMath.StepSeconds(deltaTime);
            float turnTarget = TurnTarget(SmoothTurnRate(signals.TurnRate, step, tuning.turnRateSeconds), tuning);
            float turn = _turn.Step(turnTarget, step, tuning.turnLeadSeconds, tuning.turnSettleSeconds);
            float pitch = _pitch.Step(PitchTarget(signals.LookPitch, tuning), step, tuning.pitchLookSeconds, tuning.pitchSettleSeconds);
            float move = _move.Step(MoveTarget(in signals, step, tuning, out float backward), step, tuning.moveLookSeconds, tuning.moveSettleSeconds);
            _backward.Step(backward, step, tuning.backAttack, tuning.backRelease);
            float directRight = _directRight.Step(FaceMath.ClampSigned(signals.LookRight), step, tuning.gazeAttack, tuning.gazeRelease);
            float directUp = _directUp.Step(FaceMath.ClampSigned(signals.LookUp), step, tuning.gazeAttack, tuning.gazeRelease);

            float glance = FaceMath.Clamp01(glanceDown);
            float glanceShare = tuning.glanceLookDown > 0f ? FaceMath.Clamp01(glance / tuning.glanceLookDown) : 0f;
            float pitchKept = 1f - (1f - FaceMath.Clamp01(tuning.glancePitchKeep)) * glanceShare;
            Right = FaceMath.ClampSigned(turn + move + directRight);
            Up = FaceMath.ClampSigned(pitch * pitchKept + directUp - glance);
        }

        /// <summary>Drops to looking ahead at once.</summary>
        public void Reset() {
            _turn.Reset();
            _pitch.Reset();
            _move.Reset();
            _backward.Reset();
            _directRight.Reset();
            _directUp.Reset();
            _turnRate = 0f;
            _scanElapsed = 0f;
            _scanSide = 1f;
            Right = 0f;
            Up = 0f;
        }

        /// <summary>The lead a turn at <paramref name="degreesPerSecond"/> (positive right) draws the eyes to, -1..1.</summary>
        public static float TurnTarget(float degreesPerSecond, FaceExpressionTuning tuning) {
            if (float.IsNaN(degreesPerSecond)) return 0f;

            float share = Shaped(Math.Abs(degreesPerSecond), tuning.turnDeadZone, tuning.turnFullRate);
            return Math.Sign(degreesPerSecond) * share * FaceMath.Clamp01(tuning.turnLook);
        }

        /// <summary>The look a head pitch of <paramref name="pitchDegrees"/> (positive down) draws the eyes to, -1 down..1 up.</summary>
        public static float PitchTarget(float pitchDegrees, FaceExpressionTuning tuning) {
            if (float.IsNaN(pitchDegrees)) return 0f;

            float share = Shaped(Math.Abs(pitchDegrees), tuning.pitchDeadZone, tuning.pitchFullDegrees);
            return pitchDegrees > 0f ? -share * FaceMath.Clamp01(tuning.pitchLookDown) : share * FaceMath.Clamp01(tuning.pitchLookUp);
        }

        /// <summary>
        /// How much a move of <paramref name="right"/>, <paramref name="forward"/> (facing-relative, base speeds) backs up, 0..1.
        /// </summary>
        public static float BackwardShare(float right, float forward, FaceExpressionTuning tuning) {
            float speed = Speed(right, forward);
            if (speed <= 0f) return 0f;

            return Presence(speed, tuning) * Ramp(-forward / speed, BackwardFrom, BackwardFull);
        }

        // A frame-rate independent low-pass; a bad rate counts as no turn.
        private float SmoothTurnRate(float degreesPerSecond, float step, float smoothingSeconds) {
            float rate = float.IsNaN(degreesPerSecond) || float.IsInfinity(degreesPerSecond) ? 0f : degreesPerSecond;
            float share = smoothingSeconds > 0f ? 1f - (float)Math.Exp(-step / smoothingSeconds) : 1f;
            _turnRate += (rate - _turnRate) * share;
            return _turnRate;
        }

        // Strafing glances the way the body slides; backing up adds a dart to the side walked, or side to side
        // starting on the side last walked.
        private float MoveTarget(in FaceSignalFrame signals, float step, FaceExpressionTuning tuning, out float backward) {
            float speed = Speed(signals.MoveRight, signals.MoveForward);
            backward = speed > 0f ? BackwardShare(signals.MoveRight, signals.MoveForward, tuning) : 0f;
            if (speed <= 0f) {
                _scanElapsed = 0f;
                return 0f;
            }

            float across = signals.MoveRight / speed;
            float strafe = across * Presence(speed, tuning) * FaceMath.Clamp01(tuning.strafeLook);
            bool sided = Math.Abs(across) >= SidedScan;
            if (sided) _scanSide = Math.Sign(across);
            if (sided || backward <= 0f) _scanElapsed = 0f;
            if (backward <= 0f) return strafe;

            if (!sided) _scanElapsed += step;
            return strafe + ScanSide(sided, tuning) * backward * FaceMath.Clamp01(tuning.backGlance);
        }

        private float ScanSide(bool sided, FaceExpressionTuning tuning) {
            float period = tuning.backScanSeconds;
            if (sided || !(period > 0f)) return _scanSide;

            return ((long)(_scanElapsed / period) & 1L) == 0L ? _scanSide : -_scanSide;
        }

        private static float Speed(float right, float forward) {
            if (float.IsNaN(right) || float.IsNaN(forward) || float.IsInfinity(right) || float.IsInfinity(forward)) return 0f;

            return (float)Math.Sqrt(right * right + forward * forward);
        }

        private static float Presence(float speed, FaceExpressionTuning tuning) {
            return Shaped(speed, tuning.moveDeadZone, tuning.moveFullSpeed);
        }

        // 0 inside the dead zone, rising on an ease-out to 1 at full.
        private static float Shaped(float amount, float deadZone, float full) {
            float share = Ramp(amount, deadZone, full);
            return share * (2f - share);
        }

        private static float Ramp(float amount, float from, float to) {
            if (float.IsNaN(amount) || amount <= from) return 0f;
            if (!(to > from)) return 1f;

            return FaceMath.Clamp01((amount - from) / (to - from));
        }
    }
}
