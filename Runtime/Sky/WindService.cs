using System.Threading.Tasks;
using AlpineLib.DI;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// Default <see cref="IWindService"/>: holds the session's <see cref="WindField"/> and clock and samples them
    /// at most once per frame, on first read. Previews and gates pin the time with <see cref="OverrideTime"/>.
    /// </summary>
    /// <remarks>
    /// Sampling is lazy, so it works in edit mode and needs no update ordering against its readers. A cold join
    /// further than <see cref="WindField.ReplayBudgetSteps"/> from the cache replays on a worker: <see cref="IsReady"/>
    /// is false and the drift holds its last value until the replay is taken in.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WindService : MonoBehaviour, IWindService {
        private WindField _field;
        private ISessionClock _clock;
        private double? _overrideSeconds;
        private int _sampledFrame = -1;
        private double _sampledSeconds = double.NaN;
        private Vector3 _velocity;
        private WindVector _displacement;
        private Task<WindReplay> _replay;
        private bool _isReady = true;
        private bool _isRegistered;
        private bool _isReplayBroken;

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

        public bool IsReady {
            get {
                Sample();
                return _isReady;
            }
        }

        /// <summary>True while a cold-join replay runs on a worker.</summary>
        public bool IsReplaying => _replay != null;

        [Provide]
        public IWindService ProvideWindService() {
            return this;
        }

        /// <summary>Follows <paramref name="field"/> on <paramref name="clock"/> (a new local clock when null).</summary>
        public void Configure(WindField field, ISessionClock clock) {
            _field = field;
            _clock = clock ?? new LocalSessionClock();
            _replay = null;
            _isReplayBroken = false;
            _displacement = WindVector.Zero;
            Invalidate();
        }

        /// <summary>Offers <see cref="IWindService"/> to the injector (play mode only); undone on destroy.</summary>
        public void RegisterWithInjector() {
            if (_isRegistered || !Application.isPlaying || !Injector.HasInstance) return;

            Injector.Instance.RegisterProvider(this);
            _isRegistered = true;
        }

        /// <summary>Withdraws the registration of <see cref="RegisterWithInjector"/>, if any.</summary>
        public void UnregisterFromInjector() {
            if (!_isRegistered) return;

            _isRegistered = false;
            if (Injector.HasInstance) Injector.Instance.UnregisterProvider(this);
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

        private void OnDestroy() {
            UnregisterFromInjector();
        }

        // Re-samples every read while a replay is pending, so its result is taken in as soon as it lands.
        private void Sample() {
            int frame = UnityEngine.Time.frameCount;
            if (frame == _sampledFrame && _replay == null) return;

            double seconds = _overrideSeconds ?? (_clock != null ? _clock.Seconds : 0.0);

            _sampledFrame = frame;
            _sampledSeconds = seconds;
            if (_field == null) {
                _velocity = Vector3.zero;
                _displacement = WindVector.Zero;
                _isReady = true;
                return;
            }

            WindVector velocity = _field.Velocity(seconds);
            _velocity = new Vector3((float)velocity.X, 0f, (float)velocity.Z);
            SampleDisplacement(seconds);
        }

        private void SampleDisplacement(double seconds) {
            if (_replay != null && !TryAdoptReplay()) {
                _isReady = false;
                return;
            }
            if (!_isReplayBroken && _field.StepsToReach(seconds) > WindField.ReplayBudgetSteps) {
                StartReplay(WindField.StepsOf(seconds));
                _isReady = false;
                return;
            }

            _displacement = _field.Displacement(seconds);
            _isReady = true;
        }

        private void StartReplay(long steps) {
            WindField field = _field;
            _replay = Task.Run(() => field.Replay(steps));
        }

        // False while the replay still runs; a faulted one is logged and later samples sum in place.
        private bool TryAdoptReplay() {
            if (!_replay.IsCompleted) return false;

            Task<WindReplay> replay = _replay;
            _replay = null;
            if (replay.IsFaulted) {
                Debug.LogException(replay.Exception, this);
                _isReplayBroken = true;
                return true;
            }
            if (ReferenceEquals(replay.Result.Field, _field)) _field.Adopt(replay.Result);
            return true;
        }

        private void Invalidate() {
            _sampledFrame = -1;
        }
    }
}
