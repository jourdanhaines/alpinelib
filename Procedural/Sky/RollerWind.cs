namespace AlpineLib.Procedural.Sky {
    // The wind one roll heads along at each rolled distance: the live wind at the moment the prop rolls that metre (its
    // drift mapped to time by a RollerWindClock), or a fixed prevailing direction without a clock.
    internal sealed class RollerWind {
        private readonly double _prevailingX;
        private readonly double _prevailingZ;
        private readonly RollerWindClock _clock;
        private readonly double _spawnDrift;
        private readonly double _groundSpeedShare;
        private int _hint;

        public RollerWind(WindVector prevailing, RollerWindClock clock, double spawnDrift, double groundSpeedShare) {
            _prevailingX = prevailing.X / prevailing.Length;
            _prevailingZ = prevailing.Z / prevailing.Length;
            _clock = clock;
            _spawnDrift = spawnDrift;
            _groundSpeedShare = groundSpeedShare;
        }

        /// <summary>Unit wind direction once the prop has rolled <paramref name="distance"/> metres.</summary>
        public void At(double distance, out double x, out double z) {
            if (_clock == null) {
                x = _prevailingX;
                z = _prevailingZ;
                return;
            }

            WindVector direction = _clock.DirectionAt(_spawnDrift + distance / _groundSpeedShare, ref _hint);
            x = direction.X;
            z = direction.Z;
        }
    }
}
