using System;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    // The drift a RollingPropField poses its props with: the live wind's displacement, held while the wind replays (a
    // cold join), scaled in after a wait and glided over whenever it lands farther than the wind could blow since the
    // last update.
    internal sealed class RollingPropWind {
        // Headroom on the wind's speed × real time a drift step may cover before it counts as a jump (gusts between updates).
        private const double TravelSlack = 2.0;

        private WindVector _target;
        private WindVector _glide;
        private bool _wasReady;
        private bool _hasWaited;
        private float _glideShare = 1f;
        private double _lastStep;

        /// <summary>The wind, or null (still props).</summary>
        public IWindService Service { get; private set; }

        public WindVector Displacement { get; private set; }

        /// <summary>False until the wind has given a drift (or there is no wind).</summary>
        public bool HasDisplacement { get; private set; }

        /// <summary>Scale-in share after a cold join (1 when not fading).</summary>
        public float Fade { get; private set; } = 1f;

        public bool IsGliding => _glideShare < 1f;

        /// <summary>Session seconds of the wind (real seconds without one): the runtime fades follow the clock the drift does.</summary>
        public double Now => Service != null ? Service.Time : Time.realtimeSinceStartupAsDouble;

        public void Reset(IWindService service) {
            Service = service;
            Displacement = WindVector.Zero;
            _target = WindVector.Zero;
            _glide = WindVector.Zero;
            HasDisplacement = false;
            _wasReady = false;
            _hasWaited = false;
            Fade = 1f;
            _glideShare = 1f;
            _lastStep = Time.realtimeSinceStartupAsDouble;
        }

        // Holds the last drift while the wind replays; a cold join scales the props in, any jump glides them over.
        public void Update() {
            if (Service == null) {
                HasDisplacement = true;
                return;
            }

            if (!Service.IsReady) {
                _wasReady = false;
                _hasWaited |= !HasDisplacement;
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = Math.Max(0.0, now - _lastStep);
            WindVector target = Service.Displacement;
            if (!BeginTransition(target, elapsed)) AdvanceTransition(elapsed);
            _lastStep = now;
            _target = target;
            Displacement = target + _glide * (1.0 - SmoothShare(_glideShare));
            HasDisplacement = true;
            _wasReady = true;
        }

        // A step past the wind's own travel since the last ready update (any after a hold) glides from what was drawn;
        // true when a fade or glide starts here (it advances from the next update on).
        private bool BeginTransition(WindVector target, double elapsed) {
            if (!HasDisplacement) {
                if (!_hasWaited) return false;

                Fade = 0f;
                return true;
            }

            double travel = _wasReady ? Service.Velocity.magnitude * elapsed * TravelSlack : 0.0;
            if ((target - _target).Length <= RollingPropField.JumpMetres + travel) return false;

            _glide = Displacement - target;
            _glideShare = 0f;
            return true;
        }

        private void AdvanceTransition(double elapsed) {
            float step = (float)Math.Min(RollingPropField.MaxStepSeconds, elapsed);
            if (Fade < 1f) Fade = Mathf.Min(1f, Fade + step / RollingPropField.FadeSeconds);
            if (_glideShare >= 1f) return;

            _glideShare = Mathf.Min(1f, _glideShare + step / RollingPropField.FadeSeconds);
            if (_glideShare >= 1f) _glide = WindVector.Zero;
        }

        private static double SmoothShare(float share) {
            return share * share * (3.0 - 2.0 * share);
        }
    }
}
