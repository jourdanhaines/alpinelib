using System;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.Hosting;
using AlpineLib.Server.Sessions;
using AlpineLib.Server.Sessions.Spawning;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What the front desk does when the game's own half of a session will not stand up.
    /// </summary>
    /// <remarks>
    /// A module factory and a placement factory are the game's code, running on the loop thread inside a
    /// create request. Anything they throw would otherwise travel to the loop's catch-all, which stops
    /// the process — so one player's malformed create would take every other session on the box with it.
    /// The contract these tests pin is that a refusal costs exactly that one create: the requester is
    /// denied, the half-built session is torn down rather than left holding a chat pipeline, everybody
    /// else keeps playing, and the process is never asked to stop.
    /// </remarks>
    public sealed class SessionCreationFaultTests {
        private int _placementCallCount;

        [Fact]
        public void AModuleFactoryThatThrowsRefusesTheCreateRatherThanTheProcess() {
            ThrowingSessionModuleFactory factory = new ThrowingSessionModuleFactory(0);

            using DedicatedServerHarness harness = StartHarness(factory, null);
            HarnessClient player = harness.AddClient("Driver");

            SessionJoinResult refused = RequestSession(harness, player);

            Assert.False(refused.IsSuccess);
            Assert.Equal(SessionEndReason.ServerFault, refused.Reason);
            Assert.Equal(1, factory.CreateCount);
            Assert.False(harness.WasStopRequested);
        }

        [Fact]
        public void ARefusedSessionIsNotLeftStandingInTheDirectory() {
            ThrowingSessionModuleFactory factory = new ThrowingSessionModuleFactory(0);

            using DedicatedServerHarness harness = StartHarness(factory, null);
            HarnessClient player = harness.AddClient("Driver");

            RequestSession(harness, player);

            Assert.Empty(harness.CaptureDirectory().Sessions);
        }

        [Fact]
        public void TheHalfBuiltSessionTakesItsChatPipelineDownWithIt() {
            // The entry had already started its chat pipeline by the time the game refused. Nothing will
            // ever hold that entry again, so it has to have stopped what it started before it unwound.
            ThrowingSessionModuleFactory factory = new ThrowingSessionModuleFactory(0);

            using DedicatedServerHarness harness = StartHarness(factory, null);
            HarnessClient player = harness.AddClient("Driver");

            RequestSession(harness, player);

            Assert.NotNull(factory.RefusedEntry);
            Assert.False(factory.RefusedEntry.ChatService.IsRunning);
            Assert.True(factory.RefusedEntry.Host.IsClosed);
        }

        [Fact]
        public void ASessionAlreadyRunningIsUntouchedByAnotherPlayersRefusal() {
            ThrowingSessionModuleFactory factory = new ThrowingSessionModuleFactory(1);

            using DedicatedServerHarness harness = StartHarness(factory, null);
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient stranger = harness.AddClient("Stranger");

            SessionJoinResult created = RequestSession(harness, owner);
            Assert.True(created.IsSuccess);

            Assert.False(RequestSession(harness, stranger).IsSuccess);

            // Read through the inbox rather than across the thread boundary: two answers from the loop
            // thread are two different steps, so a session that is still stepping must show more ticks.
            RecordingSessionModule survivor = factory.Modules[0];
            int ticksBefore = harness.Query(() => survivor.TickCount);

            Assert.True(
                harness.Query(() => survivor.TickCount) > ticksBefore,
                "The surviving session stopped stepping when another one was refused.");
            Assert.False(harness.Query(() => survivor.IsDisposed));
            Assert.Single(harness.CaptureDirectory().Sessions);
            Assert.False(harness.WasStopRequested);
        }

        [Fact]
        public void TheRefusedPlayerIsStillAtTheDeskAndCanJoinSomebodyElse() {
            // A refusal is a denial, not a disconnect: the connection stays authenticated, which is what
            // lets the menu offer "join a friend instead" without a second handshake.
            ThrowingSessionModuleFactory factory = new ThrowingSessionModuleFactory(1);

            using DedicatedServerHarness harness = StartHarness(factory, null);
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient stranger = harness.AddClient("Stranger");

            SessionJoinResult created = RequestSession(harness, owner);
            Assert.False(RequestSession(harness, stranger).IsSuccess);

            SessionJoinResult joined = harness.Complete(stranger.Session.JoinSessionAsync(created.JoinCode));

            Assert.True(joined.IsSuccess);
            Assert.Equal(created.SessionId, joined.SessionId);
        }

        [Fact]
        public void APlacementFactoryThatThrowsIsRefusedTheSameWay() {
            // Same seam, the other game-supplied factory: the placement is built as an argument to the
            // entry, one line earlier than the module, and must not be a way past the same guard.
            using DedicatedServerHarness harness = StartHarness(null, CreatePlacementThatRefusesTheSecondSession);
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient stranger = harness.AddClient("Stranger");

            Assert.True(RequestSession(harness, owner).IsSuccess);
            SessionJoinResult refused = RequestSession(harness, stranger);

            Assert.False(refused.IsSuccess);
            Assert.Equal(SessionEndReason.ServerFault, refused.Reason);
            Assert.Single(harness.CaptureDirectory().Sessions);
            Assert.False(harness.WasStopRequested);
        }

        [Fact]
        public void AnExportThatAsksForPointsAndNamesNoneSaysSoOnce() {
            // Silent would mean every player stood on a ring at the world origin, which on an authored
            // map is under the geometry, with nothing anywhere saying why.
            SpawnSettingsDocument spawn = new SpawnSettingsDocument { Placement = SpawnPlacementKind.List };
            CapturingLogger<SessionRegistry> logger = new CapturingLogger<SessionRegistry>();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(spawn),
                new ServerRuntimeOptions { MaxSessions = 2 },
                null,
                null,
                logger);

            Assert.True(RequestSession(harness, harness.AddClient("Driver")).IsSuccess);
            Assert.True(RequestSession(harness, harness.AddClient("Fireman")).IsSuccess);

            Assert.Equal(1, logger.CountContaining("names no points"));
        }

        [Fact]
        public void AnExportThatNamesItsPointsSaysNothing() {
            SpawnSettingsDocument spawn = new SpawnSettingsDocument {
                Placement = SpawnPlacementKind.List,
                Points = { new SpawnPointDocument { X = 3f, Y = 0f, Z = 1f } }
            };
            CapturingLogger<SessionRegistry> logger = new CapturingLogger<SessionRegistry>();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(spawn), new ServerRuntimeOptions(), null, null, logger);

            Assert.True(RequestSession(harness, harness.AddClient("Driver")).IsSuccess);

            Assert.Equal(0, logger.CountContaining("names no points"));
        }

        /// <summary>A server that will host two sessions, over whichever factories a test is probing.</summary>
        private static DedicatedServerHarness StartHarness(
            ThrowingSessionModuleFactory moduleFactory,
            Func<ServerConfigBundle, ISpawnPlacement> placementFactory) {
            return DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(),
                new ServerRuntimeOptions { MaxSessions = 2 },
                moduleFactory,
                placementFactory);
        }

        /// <summary>Connects, authenticates and asks for a session of one's own.</summary>
        private static SessionJoinResult RequestSession(DedicatedServerHarness harness, HarnessClient client) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            return harness.Complete(client.Session.CreateSessionAsync(string.Empty));
        }

        /// <summary>Seats the first session the shipped way and refuses every one after it.</summary>
        private ISpawnPlacement CreatePlacementThatRefusesTheSecondSession(ServerConfigBundle config) {
            _placementCallCount++;

            if (_placementCallCount > 1) {
                throw new InvalidOperationException("The game cannot seat this session.");
            }

            return SessionRegistry.CreateDefaultPlacement(config);
        }
    }
}
