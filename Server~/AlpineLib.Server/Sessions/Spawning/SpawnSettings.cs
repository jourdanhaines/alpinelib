using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions.Spawning;

namespace AlpineLib.Server.Sessions.Spawning {
    /// <summary>
    /// What a session gives an arriving player and where it puts them, resolved once at startup from the
    /// exported <c>spawn</c> section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the runtime shape, not the file shape: <see cref="Configuration.SpawnSettingsDocument"/>
    /// owns the JSON and maps onto this. Keeping them apart is what lets a placement be built from
    /// authored data without the netcode ever learning what a JSON document is.
    /// </para>
    /// <para>
    /// The defaults are what a game that has not authored a <c>spawn</c> section gets: prefab zero,
    /// simulated by the server, seated on a two-metre ring. That is deliberately the same behaviour the
    /// library had before the section existed, so adding the section is opt-in rather than a migration.
    /// </para>
    /// </remarks>
    public sealed class SpawnSettings {
        /// <summary>Prefab id a pawn spawns as when nothing said otherwise.</summary>
        public const ushort DefaultPawnPrefabId = 0;

        private static readonly SpawnPoint[] NoPoints = Array.Empty<SpawnPoint>();

        /// <summary>Which entry of the client's prefab registry a player's pawn is.</summary>
        public ushort PawnPrefabId { get; set; } = DefaultPawnPrefabId;

        /// <summary>Who simulates a pawn once it exists.</summary>
        public AuthorityMode PawnAuthority { get; set; } = AuthorityMode.Server;

        /// <summary>Which placement rule seats arrivals.</summary>
        public SpawnPlacementKind Placement { get; set; } = SpawnPlacementKind.Ring;

        /// <summary>Radius of the ring, in metres. Read only when <see cref="Placement"/> is a ring.</summary>
        public float RingRadius { get; set; } = RingSpawnPlacement.DefaultRadiusMetres;

        /// <summary>Seats on the ring before positions repeat. Read only when <see cref="Placement"/> is a ring.</summary>
        public int RingSeats { get; set; } = RingSpawnPlacement.DefaultSeats;

        /// <summary>Authored points, in the order arrivals take them. Read only for a list placement.</summary>
        public IReadOnlyList<SpawnPoint> Points { get; set; } = NoPoints;

        /// <summary>
        /// True when a list placement was asked for and no points came with it, so the ring answers in
        /// its place. What <see cref="SessionRegistry"/> reports to the log.
        /// </summary>
        public bool FallsBackToRing => Placement == SpawnPlacementKind.List && (Points == null || Points.Count == 0);

        /// <summary>
        /// Builds the placement these settings describe. A fresh instance every call, because a placement
        /// carries the seat counter of the one session it belongs to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A list placement with no points falls back to a ring rather than throwing: an export whose
        /// points were forgotten should seat players somewhere reasonable and be visible in a log, not
        /// stop the server from accepting anybody. <see cref="FallsBackToRing"/> is what the front desk
        /// reads to write that log line.
        /// </para>
        /// <para>
        /// <b>Shared repair rule.</b> A seat count below one becomes
        /// <see cref="RingSpawnPlacement.DefaultSeats"/>. The Unity-side twin of this type,
        /// <c>SpawnPlacementConfig.ToPlacement</c>, repairs the same exported field the same way — the
        /// two have to, or a listen host and a dedicated server built from one asset would seat their
        /// players on rings of different sizes.
        /// </para>
        /// </remarks>
        public ISpawnPlacement CreatePlacement() {
            if (Placement != SpawnPlacementKind.List || FallsBackToRing) {
                return new RingSpawnPlacement(RingRadius, RingSeats < 1 ? RingSpawnPlacement.DefaultSeats : RingSeats);
            }

            return new ListSpawnPlacement(Points);
        }
    }
}
