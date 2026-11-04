using AlpineLib.DI;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// Default <see cref="IWindService"/>: holds the session's <see cref="WindField"/> and clock and samples them
    /// at most once per frame, on first read. Previews and gates pin the time with <see cref="OverrideTime"/>.
    /// </summary>
    /// <remarks>Sampling is lazy, so it works in edit mode and needs no update ordering against its readers.</remarks>
    [DisallowMultipleComponent]
    public sealed class WindService : MonoBehaviour, IWindService {
        private WindField _field;
        private ISessionClock _clock;
        private double? _overrideSeconds;
        private int _sampledFrame = -1;
        private double _sampledSeconds = double.NaN;
        private Vector3 _velocity;
        private WindVector _displacement;

        public WindField Field => _field;

        /// <summary>The clock the wind follows; a <see cref="LocalSessionClock"/> until one is given.</summary>
        public ISessionClock Clock => _clock;

        /// <summary>True while <see cref="OverrideTime"/> pins the time.</summary>
        public bool IsTimeOverridden => _overrideSeconds.HasValue;

        public double Time {
            get {
                Sample();
                return _sampledSeconds;
            }
        }

        public Vector3 Velocity {
            get {
                Sample();
                return _velocity;
            }
        }

        public WindVector Displacement {
            get {
                Sample();
                return _displacement;
            }
        }

        [Provide]
        public IWindService ProvideWindService() {
            return this;
        }

        /// <summary>Follows <paramref name="field"/> on <paramref name="clock"/> (a new local clock when null).</summary>
        public void Configure(WindField field, ISessionClock clock) {
            _field = field;
            _clock = clock ?? new LocalSessionClock();
            Invalidate();
        }

        /// <summary>Pins the wind to <paramref name="seconds"/> of session time until <see cref="ClearTimeOverride"/>.</summary>
        public void OverrideTime(double seconds) {
            _overrideSeconds = seconds;
            Invalidate();
        }

        /// <summary>Returns the wind to its clock.</summary>
        public void ClearTimeOverride() {
            _overrideSeconds = null;
            Invalidate();
        }

        private void Sample() {
            int frame = UnityEngine.Time.frameCount;
            if (frame == _sampledFrame) return;

            double seconds = _overrideSeconds ?? (_clock != null ? _clock.Seconds : 0.0);

            _sampledFrame = frame;
            _sampledSeconds = seconds;
            if (_field == null) {
                _velocity = Vector3.zero;
                _displacement = WindVector.Zero;
                return;
            }

            WindVector velocity = _field.Velocity(seconds);
            _velocity = new Vector3((float)velocity.X, 0f, (float)velocity.Z);
            _displacement = _field.Displacement(seconds);
        }

        private void Invalidate() {
            _sampledFrame = -1;
        }
    }
}
