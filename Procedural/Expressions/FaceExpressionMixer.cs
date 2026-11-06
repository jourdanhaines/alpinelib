using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// Turns a body's signals into face channel weights each step: a scheduled blink, a wide-eyed pulse on
    /// take-off, a squash on landing that grows with the air time, worry through a long fall, a squint that
    /// follows exertion, and a gaze (<see cref="FaceGaze"/>: turn lead, pitch, strafe, backing up) with a downward glance
    /// on top. Backing up also biases the face a little worried and wide.
    /// </summary>
    /// <remarks>
    /// The expression channels share <see cref="FaceExpressionTuning.expressionBudget"/>: the newest pulse first, then
    /// the older one, worried and squint, so the reaction of the moment wins and the sum never exceeds the budget.
    /// Whatever the budget cuts carries on from the weight shown, so nothing re-rises once a reaction ends it.
    /// The back-up bias goes through the budget like the rest. Blink and the looks are independent of it: the gaze is
    /// clamped to the square |x|, |y| ≤ 1 and split onto the four look channels. Every weight is clamped to 0..1, and a face with no signals
    /// settles to exact zeros between blinks.
    /// </remarks>
    public sealed class FaceExpressionMixer {
        /// <summary>Number of <see cref="FaceChannel"/> values.</summary>
        public const int ChannelCount = 9;

        private static readonly FaceChannel[] BudgetChannels = { FaceChannel.Squash, FaceChannel.Wide, FaceChannel.Worried, FaceChannel.Squint };

        private readonly float[] _weights = new float[ChannelCount];
        private readonly FaceBlinkScheduler _blink;
        private readonly FacePulse _wide = new FacePulse();
        private readonly FacePulse _squash = new FacePulse();
        private readonly FaceFollower _worried = new FaceFollower();
        private readonly FaceFollower _squint = new FaceFollower();
        private readonly FaceFollower _glance = new FaceFollower();
        private readonly FaceGaze _gaze = new FaceGaze();
        private int _pulseSerial;
        private int _wideSerial;
        private int _squashSerial;

        /// <summary>A mixer reacting as <paramref name="tuning"/> says, its blinks drawn from <paramref name="seed"/>.</summary>
        public FaceExpressionMixer(FaceExpressionTuning tuning, uint seed) {
            Tuning = tuning ?? new FaceExpressionTuning();
            _blink = new FaceBlinkScheduler(seed, Tuning);
        }

        /// <summary>The tuning in use; edits take effect on the next step.</summary>
        public FaceExpressionTuning Tuning { get; }

        /// <summary>The blink rhythm, for inspection.</summary>
        public FaceBlinkScheduler Blink => _blink;

        /// <summary>Weights after the last step, indexed by <see cref="FaceChannel"/>.</summary>
        public IReadOnlyList<float> Weights => _weights;

        /// <summary>The gaze, for inspection.</summary>
        public FaceGaze Gaze => _gaze;

        /// <summary>Squash reached by the last landing, before the budget; 0 before any.</summary>
        public float LastLandSquash { get; private set; }

        /// <summary>Weight of one channel after the last step.</summary>
        public float WeightOf(FaceChannel channel) {
            return _weights[(int)channel];
        }

        /// <summary>Sum of the expression channels sharing the budget.</summary>
        public float ExpressionSum() {
            float sum = 0f;
            foreach (FaceChannel channel in BudgetChannels) sum += _weights[(int)channel];
            return sum;
        }

        /// <summary>True when every channel is exactly zero.</summary>
        public bool IsAtRest() {
            foreach (float weight in _weights) {
                if (weight != 0f) return false;
            }

            return true;
        }

        /// <summary>Squash a landing after <paramref name="airTime"/> seconds aloft starts toward; 0 below the minimum air time.</summary>
        public float LandSquashFor(float airTime) {
            if (!(airTime >= Tuning.landMinAirTime)) return 0f;

            float span = Tuning.landLongAirTime - Tuning.landShortAirTime;
            float along = span > 0f ? FaceMath.Clamp01((airTime - Tuning.landShortAirTime) / span) : 1f;
            return Tuning.landSquashMin + (Tuning.landSquashMax - Tuning.landSquashMin) * along;
        }

        /// <summary>Advances by <paramref name="deltaTime"/> seconds under <paramref name="signals"/> and returns the weights.</summary>
        public IReadOnlyList<float> Step(in FaceSignalFrame signals, float deltaTime) {
            float step = FaceMath.StepSeconds(deltaTime);
            TriggerPulses(in signals);

            _weights[(int)FaceChannel.Blink] = FaceMath.Clamp01(_blink.Step(step, Tuning));
            _gaze.Step(in signals, StepGlance(in signals, step), step, Tuning);
            _weights[(int)FaceChannel.Wide] = FaceMath.Clamp01(Math.Max(_wide.Step(step), BackWide()));
            _weights[(int)FaceChannel.Squash] = FaceMath.Clamp01(_squash.Step(step));
            _weights[(int)FaceChannel.Worried] = FaceMath.Clamp01(_worried.Step(WorriedTarget(in signals), step, Tuning.worriedAttack, Tuning.worriedRelease));
            _weights[(int)FaceChannel.Squint] = FaceMath.Clamp01(_squint.Step(SquintTarget(in signals), step, Tuning.squintAttack, Tuning.squintRelease));
            ApplyBudget();
            WriteLooks();
            return _weights;
        }

        /// <summary>Drops every channel to rest at once; the blink rhythm carries on.</summary>
        public void Reset() {
            _wideSerial = 0;
            _squashSerial = 0;
            _wide.Stop();
            _squash.Stop();
            _worried.Reset();
            _squint.Reset();
            _glance.Reset();
            _gaze.Reset();
            Array.Clear(_weights, 0, _weights.Length);
        }

        // A landing and a jump on one step is a hop off the ground just reached: the jump is the newer.
        private void TriggerPulses(in FaceSignalFrame signals) {
            if (signals.Landed) TriggerLanding(signals.LandAirTime);
            if (!signals.Jumped) return;

            _wide.Trigger(Tuning.jumpWide, Tuning.jumpAttack, Tuning.jumpHold, Tuning.jumpRelease, Tuning.pulseOvershoot);
            _wideSerial = ++_pulseSerial;
        }

        private void TriggerLanding(float airTime) {
            float squash = LandSquashFor(airTime);
            if (squash <= 0f) return;

            LastLandSquash = squash;
            _squash.Trigger(squash, Tuning.landAttack, Tuning.landHold, Tuning.landRelease, Tuning.pulseOvershoot);
            _squashSerial = ++_pulseSerial;
        }

        // A long fall worries; backing up (on the ground or off it) keeps a light worry of its own.
        private float WorriedTarget(in FaceSignalFrame signals) {
            float backing = _gaze.Backward * FaceMath.Clamp01(Tuning.backWorried);
            if (!signals.Airborne) return backing;

            float ramp = Tuning.worriedRampSeconds;
            float over = signals.AirTime - Tuning.worriedAirTime;
            float share = ramp > 0f ? FaceMath.Clamp01(over / ramp) : over >= 0f ? 1f : 0f;
            return Math.Max(share * Tuning.worriedPeak, backing);
        }

        private float BackWide() {
            return _gaze.Backward * FaceMath.Clamp01(Tuning.backWide);
        }

        private float SquintTarget(in FaceSignalFrame signals) {
            return signals.Airborne ? 0f : FaceMath.Clamp01(signals.Exertion) * Tuning.exertionSquint;
        }

        // Each expression keeps what fits in the budget left by those ahead of it, and its source drops to that.
        private void ApplyBudget() {
            float left = FaceMath.NonNegative(Tuning.expressionBudget);
            bool wideFirst = _wideSerial > _squashSerial;
            left = Keep(wideFirst ? FaceChannel.Wide : FaceChannel.Squash, left);
            left = Keep(wideFirst ? FaceChannel.Squash : FaceChannel.Wide, left);
            left = Keep(FaceChannel.Worried, left);
            Keep(FaceChannel.Squint, left);
            _wide.Clip(_weights[(int)FaceChannel.Wide]);
            _squash.Clip(_weights[(int)FaceChannel.Squash]);
            _worried.Clip(_weights[(int)FaceChannel.Worried]);
            _squint.Clip(_weights[(int)FaceChannel.Squint]);
        }

        private float Keep(FaceChannel channel, float left) {
            float kept = Math.Min(_weights[(int)channel], left);
            _weights[(int)channel] = kept;
            return left - kept;
        }

        private float StepGlance(in FaceSignalFrame signals, float step) {
            return _glance.Step(FaceMath.Clamp01(signals.GlanceDown) * Tuning.glanceLookDown, step, Tuning.glanceAttack, Tuning.glanceRelease);
        }

        private void WriteLooks() {
            float right = _gaze.Right;
            float up = _gaze.Up;
            _weights[(int)FaceChannel.LookRight] = FaceMath.Clamp01(right);
            _weights[(int)FaceChannel.LookLeft] = FaceMath.Clamp01(-right);
            _weights[(int)FaceChannel.LookUp] = FaceMath.Clamp01(up);
            _weights[(int)FaceChannel.LookDown] = FaceMath.Clamp01(-up);
        }

    }
}
