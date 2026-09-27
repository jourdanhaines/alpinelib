using System.Linq;
using System.Numerics;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Server.Hosting;
using AlpineLib.Server.Sessions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// End to end on a real dedicated server: the host's params reach the session and every joiner, a
    /// game factory can take over placement, and a module can respawn a member.
    /// </summary>
    public sealed class SessionParamsLoopbackTests {
        private static readonly byte[] SeedParams = { 1, 0x2A, 0, 0, 0, 0, 0, 0, 0, 3, 0 };
        private static readonly Vector3 FactorySpot = new Vector3(40f, 2f, -12f);

        [Fact]
        public void TheHostsParamsAreEchoedToTheHostAndEveryJoiner() {
            PlacementSessionModuleFactory factory = new PlacementSessionModuleFactory(entry => null);
            using DedicatedServerHarness harness = StartHarness(factory);
            HarnessClient owner = harness.AddClient("Owner");
            HarnessClient guest = harness.AddClient("Guest");

            SessionJoinResult created = Host(harness, owner, SeedParams);
            Connect(harness, guest);
            SessionJoinResult joined = harness.Complete(guest.Session.JoinSessionAsync(created.JoinCode));

            Assert.True(joined.IsSuccess);
            Assert.Equal(SeedParams, owner.Session.SessionParams);
            Assert.Equal(SeedParams, guest.Session.SessionParams);
            Assert.Equal(SeedParams, harness.Query(() => factory.Entries[0].CreateParams));
            Assert.Equal(SeedParams, harness.Query(() => factory.ParamsSeen[0]));
        }

        [Fact]
        public void ASessionCreatedWithoutParamsEchoesAnEmptyBlob() {
            PlacementSessionModuleFactory factory = new PlacementSessionModuleFactory(entry => null);
            using DedicatedServerHarness harness = StartHarness(factory);
            HarnessClient owner = harness.AddClient("Owner");

            Connect(harness, owner);
            Assert.True(harness.Complete(owner.Session.CreateSessionAsync(string.Empty)).IsSuccess);

            Assert.Empty(owner.Session.SessionParams);
            Assert.Empty(harness.Query(() => factory.Entries[0].CreateParams));
        }

        [Fact]
        public void AFactoryPlacementSeatsArrivalsInsteadOfTheConfiguredOne() {
            RecordingSpawnPlacement placement = new RecordingSpawnPlacement(FactorySpot);
            PlacementSessionModuleFactory factory = new PlacementSessionModuleFactory(entry => placement);
            using DedicatedServerHarness harness = StartHarness(factory);
            HarnessClient owner = harness.AddClient("Owner");

            Host(harness, owner, SeedParams);
            Assert.True(harness.PumpUntil(() => owner.HasLocalPeerId), "The owner was never seated.");

            SessionEntry entry = factory.Entries[0];
            NetEntity pawn = harness.Query(() => entry.Replication.Entities.Entities.Single());

            Assert.Equal(FactorySpot, pawn.State.Position);
            Assert.Same(placement, harness.Query(() => entry.Spawner.Placement));
            Assert.Equal(new[] { true }, harness.Query(() => factory.SawOwnModule.ToArray()));
        }

        [Fact]
        public void AFactoryThatAnswersNullKeepsTheConfiguredPlacement() {
            PlacementSessionModuleFactory factory = new PlacementSessionModuleFactory(entry => null);
            using DedicatedServerHarness harness = StartHarness(factory);
            HarnessClient owner = harness.AddClient("Owner");

            Host(harness, owner, SeedParams);

            Assert.IsType<RingSpawnPlacement>(harness.Query(() => factory.Entries[0].Spawner.Placement));
        }

        [Fact]
        public void APlacementHookThatThrowsRefusesTheCreate() {
            PlacementSessionModuleFactory factory = new PlacementSessionModuleFactory(entry => throw new System.InvalidOperationException("no world"));
            using DedicatedServerHarness harness = StartHarness(factory);
            HarnessClient owner = harness.AddClient("Owner");

            Connect(harness, owner);
            SessionJoinResult refused = harness.Complete(owner.Session.CreateSessionAsync(string.Empty, SeedParams));

            Assert.False(refused.IsSuccess);
            Assert.Equal(SessionEndReason.ServerFault, refused.Reason);
            Assert.Empty(harness.CaptureDirectory().Sessions);
            Assert.False(harness.WasStopRequested);
        }

        [Fact]
        public void ARespawnReplacesTheBodyThroughThePlacementAsARejoin() {
            RecordingSpawnPlacement placement = new RecordingSpawnPlacement(FactorySpot);
            PlacementSessionModuleFactory factory = new PlacementSessionModuleFactory(entry => placement);
            using DedicatedServerHarness harness = StartHarness(factory);
            HarnessClient owner = harness.AddClient("Owner");

            Host(harness, owner, SeedParams);
            Assert.True(harness.PumpUntil(() => owner.HasLocalPeerId), "The owner was never seated.");

            SessionEntry entry = factory.Entries[0];
            uint firstPawn = harness.Query(() => entry.Replication.Entities.Entities.Single().Id);

            Assert.True(harness.Query(() => entry.Spawner.Respawn(owner.PlayerId)));

            NetEntity secondPawn = harness.Query(() => entry.Replication.Entities.Entities.Single());
            Assert.NotEqual(firstPawn, secondPawn.Id);
            Assert.True(harness.Query(() => entry.Spawner.TryGetPawn(owner.PlayerId, out uint id) && id == secondPawn.Id));
            Assert.Equal(new[] { false, true }, harness.Query(() => placement.RejoinFlags.ToArray()));
            Assert.True(harness.PumpUntil(() => owner.FindOwnPawn() != null && owner.FindOwnPawn().Id == secondPawn.Id),
                "The owner never saw its new body.");
        }

        [Fact]
        public void ARespawnForSomebodyNotInTheSessionIsRefused() {
            PlacementSessionModuleFactory factory = new PlacementSessionModuleFactory(entry => null);
            using DedicatedServerHarness harness = StartHarness(factory);
            HarnessClient owner = harness.AddClient("Owner");

            Host(harness, owner, SeedParams);

            Assert.False(harness.Query(() => factory.Entries[0].Spawner.Respawn(PlayerId.NewId())));
        }

        private static DedicatedServerHarness StartHarness(PlacementSessionModuleFactory factory) {
            return DedicatedServerHarness.Start(DedicatedServerHarness.BuildConfig(), new ServerRuntimeOptions(), factory);
        }

        private static void Connect(DedicatedServerHarness harness, HarnessClient client) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
        }

        private static SessionJoinResult Host(DedicatedServerHarness harness, HarnessClient owner, byte[] sessionParams) {
            Connect(harness, owner);
            SessionJoinResult created = harness.Complete(owner.Session.CreateSessionAsync(string.Empty, sessionParams));
            Assert.True(created.IsSuccess);
            return created;
        }
    }
}
