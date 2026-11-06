using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// Turns a body's signals into face channel weights each step: a scheduled blink, a wide-eyed pulse on
    /// take-off, a squash on landing that grows with the air time, worry through a long fall, a squint that
    /// follows exertion, and a gaze with a downward glance on top.
    /// </summary>
    /// <remarks>
    /// The expression channels share <see cref="FaceExpressionTuning.expressionBudget"/>, filled in priority
    /// order (squash, wide, worried, squint) so the reaction of the moment wins and the sum never exceeds the
    /// budget. Blink and the looks are independent of it. Every weight is clamped to 0..1, and a face with no
    /// signals settles to exact zeros between blinks.
    /// </remarks>
    public sealed class FaceExpressionMixer {
        /// <summary>Number of <see cref="FaceChannel"/> values.</summary>
        public const int ChannelCount = 9;

        private static readonly FaceChannel[] BudgetOrder = { FaceChannel.Squash, FaceChannel.Wide, FaceChannel.Worried, FaceChannel.Squint };

        private readonly float[] _weights = new float[ChannelCount];
        private readonly FaceBlinkScheduler _blink;
        private readonly FacePulse _wide = new FacePulse();
        private readonly FacePulse _squash = new FacePulse();
        private readonly FaceFollower _worried = new FaceFollower();
        private readonly FaceFollower _squint = new FaceFollower();
        private readonly FaceFollower _glance = new FaceFollower();
        private readonly FaceFollower _gazeRight = new FaceFollower();
        private readonly FaceFollower _gazeUp = new FaceFollower();

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

        /// <summary>Squash reached by the last landing, before the budget; 0 before any.</summary>
        public float LastLandSquash { get; private set; }

        /// <summary>Weight of one channel after the last step.</summary>
        public float WeightOf(FaceChannel channel) {
            return _weights[(int)channel];
        }

        /// <summary>Sum of the expression channels sharing the budget.</summary>
        public float ExpressionSum() {
            float sum = 0f;
            foreach (FaceChannel channel in BudgetOrder) sum += _weights[(int)channel];
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
            float along = span > 0f ? Clamp01((airTime - Tuning.landShortAirTime) / span) : 1f;
            return Tuning.landSquashMin + (Tuning.landSquashMax - Tuning.landSquashMin) * along;
        }

        /// <summary>Advances by <paramref name="deltaTime"/> seconds under <paramref name="signals"/> and returns the weights.</summary>
        public IReadOnlyList<float> Step(in FaceSignalFrame signals, float deltaTime) {
            float step = deltaTime > 0f && !float.IsInfinity(deltaTime) ? deltaTime : 0f;
            TriggerPulses(in signals);

            _weights[(int)FaceChannel.Blink] = Clamp01(_blink.Step(step, Tuning));
            _weights[(int)FaceChannel.Wide] = Clamp01(_wide.Step(step));
            _weights[(int)FaceChannel.Squash] = Clamp01(_squash.Step(step));
            _weights[(int)FaceChannel.Worried] = Clamp01(_worried.Step(WorriedTarget(in signals), step, Tuning.worriedAttack, Tuning.worriedRelease));
            _weights[(int)FaceChannel.Squint] = Clamp01(_squint.Step(SquintTarget(in signals), step, Tuning.squintAttack, Tuning.squintRelease));
            ApplyBudget();
            WriteLooks(in signals, step);
            return _weights;
        }

        /// <summary>Drops every channel to rest at once; the blink rhythm carries on.</summary>
        public void Reset() {
            _wide.Stop();
            _squash.Stop();
            _worried.Reset();
            _squint.Reset();
            _glance.Reset();
            _gazeRight.Reset();
            _gazeUp.Reset();
            Array.Clear(_weights, 0, _weights.Length);
        }

        private void TriggerPulses(in FaceSignalFrame signals) {
            if (signals.Jumped) _wide.Trigger(Tuning.jumpWide, Tuning.jumpAttack, Tuning.jumpHold, Tuning.jumpRelease, Tuning.pulseOvershoot);
            if (!signals.Landed) return;

            float squash = LandSquashFor(signals.LandAirTime);
            if (squash <= 0f) return;

            LastLandSquash = squash;
            _squash.Trigger(squash, Tuning.landAttack, Tuning.landHold, Tuning.landRelease, Tuning.pulseOvershoot);
        }

        private float WorriedTarget(in FaceSignalFrame signals) {
            if (!signals.Airborne) return 0f;

            float ramp = Tuning.worriedRampSeconds;
            float over = signals.AirTime - Tuning.worriedAirTime;
            float share = ramp > 0f ? Clamp01(over / ramp) : over >= 0f ? 1f : 0f;
            return share * Tuning.worriedPeak;
        }

        private float SquintTarget(in FaceSignalFrame signals) {
            return signals.Airborne ? 0f : Clamp01(signals.Exertion) * Tuning.exertionSquint;
        }

        // Each expression keeps what fits in the budget left by those ahead of it.
        private void ApplyBudget() {
            float left = Math.Max(Tuning.expressionBudget, 0f);
            foreach (FaceChannel channel in BudgetOrder) {
                float kept = Math.Min(_weights[(int)channel], left);
                _weights[(int)channel] = kept;
                left -= kept;
            }
        }

        private void WriteLooks(in FaceSignalFrame signals, float step) {
            float glance = _glance.Step(Clamp01(signals.GlanceDown) * Tuning.glanceLookDown, step, Tuning.glanceAttack, Tuning.glanceRelease);
            float right = _gazeRight.Step(ClampSigned(signals.LookRight), step, Tuning.gazeAttack, Tuning.gazeRelease);
            float up = _gazeUp.Step(ClampSigned(signals.LookUp), step, Tuning.gazeAttack, Tuning.gazeRelease);
            float vertical = ClampSigned(up - glance);
            _weights[(int)FaceChannel.LookRight] = Clamp01(right);
            _weights[(int)FaceChannel.LookLeft] = Clamp01(-right);
            _weights[(int)FaceChannel.LookUp] = Clamp01(vertical);
            _weights[(int)FaceChannel.LookDown] = Clamp01(-vertical);
        }

        private static float Clamp01(float value) {
            if (float.IsNaN(value)) return 0f;

            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        private static float ClampSigned(float value) {
            if (float.IsNaN(value)) return 0f;

            return value < -1f ? -1f : value > 1f ? 1f : value;
        }
    }
}
