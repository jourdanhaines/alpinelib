namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One timed appearance of a wind-rolled prop (<see cref="RollerSpawner"/>): its cell, the wind drift it appears at,
    /// its seed and the traits drawn from it before any world is known (size, ground speed share, life), and the drift by
    /// which it has rolled its whole life. A pure function of the world seed, the cell and the drift.
    /// </summary>
    public readonly struct RollerSpawn {
        public RollerSpawn(ulong id, long cellX, long cellZ, long bucket, int index, double drift, double size, double groundSpeedShare, double life) {
            Id = id;
            CellX = cellX;
            CellZ = cellZ;
            Bucket = bucket;
            Index = index;
            Drift = drift;
            Size = size;
            GroundSpeedShare = groundSpeedShare;
            Life = life;
        }

        /// <summary>Unique per world, cell, bucket and index; also the prop's seed.</summary>
        public ulong Id { get; }

        public long CellX { get; }
        public long CellZ { get; }
        public long Bucket { get; }
        public int Index { get; }

        /// <summary>Drift metres (wind travel along the prevailing direction since the session began) the prop appears at.</summary>
        public double Drift { get; }

        public double Size { get; }
        public double GroundSpeedShare { get; }

        /// <summary>Most metres it rolls (less when blocked).</summary>
        public double Life { get; }

        /// <summary>Drift by which its whole life is rolled.</summary>
        public double EndDrift => Drift + Life / GroundSpeedShare;

        /// <summary>Metres rolled at drift <paramref name="drift"/> (negative before it appears).</summary>
        public double RolledAt(double drift) {
            return (drift - Drift) * GroundSpeedShare;
        }

        /// <summary>True when it is out (appeared and not past its whole life) at <paramref name="drift"/>.</summary>
        public bool IsAliveAt(double drift) {
            return drift >= Drift && drift < EndDrift;
        }

        /// <summary>Spawn order: earlier drift first, then id.</summary>
        public bool IsBefore(RollerSpawn other) {
            return Drift < other.Drift || (Drift == other.Drift && Id < other.Id);
        }
    }
}
