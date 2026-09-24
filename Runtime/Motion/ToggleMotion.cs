using System;
using LitMotion;
using UnityEngine;

namespace AlpineLib.Motion {
    /// <summary>
    /// A reversible 0..1 tween that always starts from wherever it currently is.
    /// </summary>
    /// <remarks>
    /// State is a fraction of travel rather than a position so one toggle can drive a slide, a swing or
    /// a fade alike. Retargeting cancels the running motion and starts a new one from the current
    /// fraction, scaled to the distance left, so a door toggled mid-travel reverses at the same speed
    /// instead of jumping to an end pose. Outside play mode LitMotion only ticks on the editor update
    /// loop, which never runs inside a synchronous batchmode call, so every move there snaps.
    /// </remarks>
    public class ToggleMotion {
        private const float ArrivedEpsilon = 0.0001f;

        private readonly Action<float> _apply;
        private readonly float _fullDurationSeconds;
        private readonly Ease _ease;
        private MotionHandle _handle;

        /// <summary>The fraction most recently applied, 0 closed through 1 open.</summary>
        public float Fraction { get; private set; }

        /// <summary>The fraction the toggle is heading for, or resting at.</summary>
        public float Target { get; private set; }

        /// <summary>True while a tween is running towards <see cref="Target"/>.</summary>
        public bool IsMoving => _handle.IsActive();

        /// <param name="apply">Receives every fraction as it is applied.</param>
        /// <param name="fullDurationSeconds">Seconds for a full 0 to 1 travel; shorter moves take proportionally less.</param>
        /// <param name="ease">Curve for each individual move.</param>
        public ToggleMotion(Action<float> apply, float fullDurationSeconds, Ease ease = Ease.InOutSine) {
            _apply = apply;
            _fullDurationSeconds = Mathf.Max(fullDurationSeconds, 0f);
            _ease = ease;
        }

        /// <summary>Tweens from the current fraction to the target, snapping when not playing or already there.</summary>
        public void MoveTo(float target) {
            Cancel();
            Target = target;

            float remaining = Mathf.Abs(target - Fraction);
            float duration = _fullDurationSeconds * remaining;
            if (!Application.isPlaying || remaining <= ArrivedEpsilon || duration <= 0f) {
                Snap(target);
                return;
            }

            _handle = LMotion.Create(Fraction, target, duration)
                .WithEase(_ease)
                .Bind(this, (value, self) => self.Apply(value));
        }

        /// <summary>Cancels any running move and applies the fraction immediately.</summary>
        public void Snap(float fraction) {
            Cancel();
            Target = fraction;
            Apply(fraction);
        }

        /// <summary>Stops a running move where it is; <see cref="Fraction"/> keeps the last applied value.</summary>
        public void Cancel() {
            _handle.TryCancel();
            _handle = default;
        }

        private void Apply(float fraction) {
            Fraction = fraction;
            _apply?.Invoke(fraction);
        }
    }
}
