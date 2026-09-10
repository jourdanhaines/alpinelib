using System;
using System.IO;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.Sessions.Spawning;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Covers the one place the Unity-authored world and the .NET server meet: the exported JSON.
    /// </summary>
    /// <remarks>
    /// The failure this file exists to prevent is silent. A key that does not map, or an enum read
    /// positionally rather than by name, produces a server that starts happily and then plays by rules
    /// no client was shipped with — so the assertions are about the mapping itself, key by key, and
    /// about refusing to start when the file is not there.
    /// </remarks>
    public sealed class SessionConfigLoaderTests {
        /// <summary>
        /// The exact shape the session-config exporter writes: objects mirroring the shared types, enums
        /// as their numeric values.
        /// </summary>
        private const string ExportedConfig = @"{
  ""net"": {
    ""gameProtocolName"": ""allaboard"", ""port"": 9051, ""maxPeers"": 12, ""serverTickRate"": 30,
    ""snapshotRate"": 15, ""clientSendRate"": 30, ""interpolationDelayMs"": 120,
    ""disconnectTimeoutMs"": 4000, ""movementToleranceMultiplier"": 1.25,
    ""movementProfiles"": [ { ""displayName"": ""Actor"", ""walkSpeed"": 2.5, ""sprintSpeed"": 6.5,
                             ""capsuleRadius"": 0.4, ""capsuleHeight"": 1.3, ""stepOffset"": 0.25,
                             ""slopeLimitDegrees"": 55 } ]
  },
  ""session"": {
    ""profile"": { ""profileId"": ""allaboard"", ""lifetimeMode"": 1, ""hostPolicy"": 1, ""rejoinPolicy"": 2,
                   ""rejoinWindowSeconds"": 90, ""maxPlayers"": 6, ""readyTimeoutSeconds"": 25,
                   ""lateLoadPolicy"": 1, ""allowJoinDuringMatch"": false, ""resultsHoldSeconds"": 7,
                   ""emptyShutdownSeconds"": 240 },
    ""lobby"": { ""displayName"": ""Depot"", ""lobbySceneName"": ""Game"", ""lobbyCapacity"": 6,
                 ""ownerCanKick"": true, ""ownerLaunchesMatches"": true },
    ""matches"": [ { ""matchId"": ""haul"", ""displayName"": ""Haul"", ""sceneName"": ""Game"",
                     ""minPlayers"": 2, ""maxPlayers"": 6, ""maxDurationSeconds"": 180 } ],
    ""defaultDisplayName"": ""Driver"",
    ""authMode"": 0
  },
  ""chat"": { ""maxMessageLength"": 140, ""historyOnJoinCount"": 16, ""profanityWordList"": [ ""coal"" ] },
  ""spawn"": { ""pawnPrefabId"": 2, ""pawnAuthority"": 1, ""placement"": 1, ""ringRadius"": 3.5,
               ""ringSeats"": 4,
               ""points"": [ { ""x"": 1, ""y"": 2, ""z"": 3, ""yawDegrees"": 90 },
                             { ""x"": -4, ""y"": 0, ""z"": 5, ""yawDegrees"": 180 } ] }
}";

        [Fact]
        public void EveryAuthoredSectionReachesTheRuntimeObjectItBelongsTo() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(ExportedConfig, "test.json");

            Assert.Equal(9051, bundle.Net.Port);
            Assert.Equal(12, bundle.Net.MaxPeers);
            Assert.Equal(120, bundle.Net.InterpolationDelayMs);
            Assert.Equal(1.25f, bundle.Net.MovementToleranceMultiplier);

            SessionProfileData profile = bundle.Session.Profile;
            Assert.Equal(SessionLifetimeMode.LobbyScoped, profile.LifetimeMode);
            Assert.Equal(HostPolicy.TransferToMember, profile.HostPolicy);
            Assert.Equal(RejoinPolicy.AnyTime, profile.RejoinPolicy);
            Assert.Equal(LateLoadPolicy.Disconnect, profile.LateLoadPolicy);
            Assert.Equal(6, profile.MaxPlayers);
            Assert.Equal(240f, profile.EmptyShutdownSeconds);

            Assert.Equal("Game", bundle.Session.Lobby.LobbySceneName);
            Assert.Equal("Driver", bundle.Session.DefaultDisplayName);
            MatchDefinitionData match = Assert.Single(bundle.Session.Matches);
            Assert.Equal("haul", match.MatchId);
            Assert.Equal(180f, match.MaxDurationSeconds);

            Assert.Equal(140, bundle.Chat.MaxMessageLength);
            Assert.Equal(16, bundle.Chat.HistoryOnJoinCount);
            Assert.Equal(new[] { "coal" }, bundle.Chat.ProfanityWordList);
        }

        [Fact]
        public void TheSpawnSectionDecidesWhatAnArrivalIsAndWhereItStands() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(ExportedConfig, "test.json");
            SpawnSettings spawn = bundle.Spawn;

            Assert.Equal(2, spawn.PawnPrefabId);
            Assert.Equal(AuthorityMode.OwnerClient, spawn.PawnAuthority);
            Assert.Equal(SpawnPlacementKind.List, spawn.Placement);
            Assert.Equal(3.5f, spawn.RingRadius);
            Assert.Equal(4, spawn.RingSeats);
            Assert.Equal(2, spawn.Points.Count);
            Assert.Equal(1f, spawn.Points[0].Position.X);
            Assert.Equal(2f, spawn.Points[0].Position.Y);
            Assert.Equal(3f, spawn.Points[0].Position.Z);
            Assert.Equal(90f, spawn.Points[0].YawDegrees);
            Assert.Equal(-4f, spawn.Points[1].Position.X);
        }

        [Fact]
        public void AListSectionBuildsTheListPlacementItDescribed() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(ExportedConfig, "test.json");

            ISpawnPlacement placement = bundle.Spawn.CreatePlacement();

            ListSpawnPlacement list = Assert.IsType<ListSpawnPlacement>(placement);
            Assert.Equal(2, list.Points.Count);
        }

        [Fact]
        public void EachSessionGetsAPlacementOfItsOwn() {
            // A placement carries the seat counter of one session. Sharing one would seat the second
            // session's first arrival wherever the first session's last one stood.
            ServerConfigBundle bundle = SessionConfigLoader.Parse("{ }", "test.json");

            Assert.NotSame(bundle.Spawn.CreatePlacement(), bundle.Spawn.CreatePlacement());
        }

        [Fact]
        public void AConfigWithNoSpawnSectionSeatsPlayersTheWayTheLibraryAlwaysDid() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse("{ \"net\": { \"port\": 9050 } }", "test.json");
            SpawnSettings spawn = bundle.Spawn;

            Assert.Equal(0, spawn.PawnPrefabId);
            Assert.Equal(AuthorityMode.Server, spawn.PawnAuthority);
            Assert.Equal(SpawnPlacementKind.Ring, spawn.Placement);
            Assert.Equal(RingSpawnPlacement.DefaultRadiusMetres, spawn.RingRadius);
            Assert.Equal(RingSpawnPlacement.DefaultSeats, spawn.RingSeats);
            Assert.Empty(spawn.Points);

            RingSpawnPlacement ring = Assert.IsType<RingSpawnPlacement>(spawn.CreatePlacement());
            Assert.Equal(RingSpawnPlacement.DefaultRadiusMetres, ring.RadiusMetres);
            Assert.Equal(RingSpawnPlacement.DefaultSeats, ring.Seats);
        }

        [Fact]
        public void ASpawnSectionThatAsksForAListAndNamesNoPointsFallsBackToTheRing() {
            // An export whose points were forgotten should seat players somewhere reasonable and be
            // visible in a log, not stop the server from accepting anybody.
            ServerConfigBundle bundle = SessionConfigLoader.Parse(
                "{ \"spawn\": { \"placement\": \"List\", \"points\": [] } }", "test.json");

            Assert.Equal(SpawnPlacementKind.List, bundle.Spawn.Placement);
            Assert.IsType<RingSpawnPlacement>(bundle.Spawn.CreatePlacement());
        }

        [Fact]
        public void SpawnEnumsAreTakenByNameAsWellAsByNumber() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(
                "{ \"spawn\": { \"pawnAuthority\": \"OwnerClient\", \"placement\": \"Ring\", \"ringSeats\": 12 } }",
                "test.json");

            Assert.Equal(AuthorityMode.OwnerClient, bundle.Spawn.PawnAuthority);
            Assert.Equal(SpawnPlacementKind.Ring, bundle.Spawn.Placement);
            Assert.Equal(12, bundle.Spawn.RingSeats);
        }

        [Fact]
        public void ExportedProfileOrderBecomesTheMovementEnvelopeIndexedByPrefabId() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(ExportedConfig, "test.json");

            MovementProfile profile = bundle.Net.GetMovementProfile(0);
            Assert.NotNull(profile);
            Assert.Equal(2.5f, profile.WalkSpeed);
            Assert.Equal(6.5f, profile.SprintSpeed);

            // Unauthored gait speeds keep the shipped defaults rather than collapsing to zero, which would
            // make the validator refuse every step a client took.
            Assert.Equal(1.0f, profile.WalkSlowSpeed);
            Assert.Null(bundle.Net.GetMovementProfile(1));
        }

        [Fact]
        public void TheCollisionCapsuleCrossesTheExportBoundaryWithTheGaitSpeeds() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(ExportedConfig, "test.json");

            MovementProfile profile = bundle.Net.GetMovementProfile(0);

            // The server sweeps this capsule through the scene geometry while the owning client sweeps
            // its own copy. A radius that only one end knows about is a wall only one end stops at.
            Assert.Equal(0.4f, profile.CapsuleRadius);
            Assert.Equal(1.3f, profile.CapsuleHeight);
            Assert.Equal(0.25f, profile.StepOffset);
            Assert.Equal(55f, profile.SlopeLimitDegrees);
        }

        [Fact]
        public void AProfileExportedBeforeCapsulesExistedKeepsTheShippedDimensions() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(
                "{ \"net\": { \"movementProfiles\": [ { \"displayName\": \"Actor\" } ] } }", "test.json");

            MovementProfile profile = bundle.Net.GetMovementProfile(0);

            Assert.Equal(0.35f, profile.CapsuleRadius);
            Assert.Equal(1.1f, profile.CapsuleHeight);
            Assert.Equal(0.3f, profile.StepOffset);
            Assert.Equal(50f, profile.SlopeLimitDegrees);
        }

        [Fact]
        public void AnExportWhoseSendRateFightsItsTickRateIsRefusedAtLoad() {
            // The one cross-field rule the export can break: the owning client would predict one motor
            // step per send while the server took two per second more than that. Catching it here costs a
            // startup message; missing it costs a rubber-band with no error anywhere behind it.
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => SessionConfigLoader.Parse(
                    "{ \"net\": { \"serverTickRate\": 30, \"clientSendRate\": 20 } }", "test.json"));

            Assert.Contains("clientSendRate", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AMissingSectionFallsBackToTheShippedDefaults() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse("{ }", "test.json");

            Assert.Equal(9050, bundle.Net.Port);
            Assert.Equal(30, bundle.Net.ServerTickRate);
            Assert.Equal(8, bundle.Session.Profile.MaxPlayers);
            Assert.Equal(200, bundle.Chat.MaxMessageLength);
            Assert.Empty(bundle.Session.Matches);
        }

        [Fact]
        public void ADefaultProtocolNameIsNotAnyRealGamesName() {
            // A server booted with no config must not be able to answer another game's clients.
            ServerConfigBundle bundle = SessionConfigLoader.Parse("{ }", "test.json");

            Assert.Equal("alpine", bundle.Net.GameProtocolName);
        }

        [Fact]
        public void KeyCasingFromTheExporterDoesNotDecideWhetherTheServerBoots() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse("{ \"Net\": { \"Port\": 9060 } }", "test.json");

            Assert.Equal(9060, bundle.Net.Port);
        }

        [Fact]
        public void AnUnknownKeyFromANewerExporterStillBootsAnOlderServer() {
            ServerConfigBundle bundle = SessionConfigLoader.Parse(
                "{ \"net\": { \"port\": 9061 }, \"weather\": { \"fog\": true } }", "test.json");

            Assert.Equal(9061, bundle.Net.Port);
        }

        [Fact]
        public void AMissingFileStopsTheServerInsteadOfInventingRules() {
            string path = Path.Combine(Path.GetTempPath(), "alpine-missing-" + Guid.NewGuid().ToString("N") + ".json");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => SessionConfigLoader.LoadFromFile(path));
            Assert.Contains("was not found", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnsetPathIsReportedAsNobodyHavingConfiguredOne() {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => SessionConfigLoader.LoadFromFile(string.Empty));

            Assert.Contains("--config", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void MalformedJsonIsReportedAgainstTheFileItCameFrom() {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => SessionConfigLoader.Parse("{ \"net\": ", "broken.json"));

            Assert.Contains("broken.json", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AConfigReadOffDiskIsTheSameAsOneParsedInMemory() {
            string path = Path.Combine(Path.GetTempPath(), "alpine-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, ExportedConfig);

            try {
                ServerConfigBundle bundle = SessionConfigLoader.LoadFromFile(path);

                Assert.Equal(9051, bundle.Net.Port);
                Assert.Equal(2, bundle.Spawn.PawnPrefabId);
                Assert.Equal(Path.GetFullPath(path), bundle.SourcePath);
            }
            finally {
                File.Delete(path);
            }
        }
    }
}
