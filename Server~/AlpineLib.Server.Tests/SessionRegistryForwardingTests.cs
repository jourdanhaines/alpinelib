using AlpineLib.Netcode.Sessions;
using AlpineLib.Server.Sessions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Covers what the front desk exists for: many sessions on one socket, and every message reaching the
    /// one its sender is in.
    /// </summary>
    /// <remarks>
    /// The bug this file is written against is the quiet one — a handler that acts on a message without
    /// asking which session the sender belongs to. On a single-session server that is invisible; on a
    /// process hosting two, one player's lever pulls the other session's train.
    /// </remarks>
    public sealed class SessionRegistryForwardingTests {
        private const ushort DriverSlot = 7;

        [Fact]
        public void AClaimIsArbitratedInsideTheSenderSSessionAndNowhereElse() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient driver = harness.AddClient("Driver");
            HarnessClient stranger = harness.AddClient("Stranger");

            OpenOwnSession(harness, driver);
            OpenOwnSession(harness, stranger);

            driver.Claims.RequestClaim(DriverSlot);

            Assert.True(
                harness.PumpUntil(() => driver.Claims.IsHeldLocally(DriverSlot)),
                "The claim never came back to the peer that asked for it.");

            // Two sessions, two registries: the stranger's own lever seven is still free, and taking it
            // does not turn into a refusal because somebody in another session got there first.
            harness.Pump(5);
            Assert.False(stranger.Claims.IsHeldLocally(DriverSlot));
            Assert.Equal(ClientClaimsFreeHolder, stranger.Claims.Holder(DriverSlot));

            stranger.Claims.RequestClaim(DriverSlot);
            Assert.True(
                harness.PumpUntil(() => stranger.Claims.IsHeldLocally(DriverSlot)),
                "The same slot number in another session was refused.");
        }

        [Fact]
        public void ASecondPlayerInTheSameSessionIsRefusedTheSlot() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient driver = harness.AddClient("Driver");
            HarnessClient mate = harness.AddClient("Mate");

            string joinCode = OpenOwnSession(harness, driver);
            JoinSession(harness, mate, joinCode);

            driver.Claims.RequestClaim(DriverSlot);
            Assert.True(harness.PumpUntil(() => mate.Claims.Holder(DriverSlot) == driver.LocalPeerId),
                "The verdict never reached the rest of the session.");

            mate.Claims.RequestClaim(DriverSlot);
            harness.Pump(10);

            Assert.False(mate.Claims.IsHeldLocally(DriverSlot));
            Assert.Equal(driver.LocalPeerId, mate.Claims.Holder(DriverSlot));
        }

        [Fact]
        public void ReleasingASlotFreesItForEverybodyElse() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient driver = harness.AddClient("Driver");
            HarnessClient mate = harness.AddClient("Mate");

            string joinCode = OpenOwnSession(harness, driver);
            JoinSession(harness, mate, joinCode);

            driver.Claims.RequestClaim(DriverSlot);
            harness.PumpUntil(() => mate.Claims.Holder(DriverSlot) == driver.LocalPeerId);

            driver.Claims.Release(DriverSlot);
            Assert.True(harness.PumpUntil(() => mate.Claims.Holder(DriverSlot) == ClientClaimsFreeHolder),
                "The release never reached the rest of the session.");

            mate.Claims.RequestClaim(DriverSlot);
            Assert.True(harness.PumpUntil(() => mate.Claims.IsHeldLocally(DriverSlot)), "The freed slot was not takeable.");
        }

        [Fact]
        public void AGracefulLeaveGivesUpEverySlotThePlayerHeld() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient driver = harness.AddClient("Driver");
            HarnessClient mate = harness.AddClient("Mate");

            string joinCode = OpenOwnSession(harness, driver);
            JoinSession(harness, mate, joinCode);

            driver.Claims.RequestClaim(DriverSlot);
            harness.PumpUntil(() => mate.Claims.Holder(DriverSlot) == driver.LocalPeerId);

            harness.Complete(driver.Session.LeaveAsync());

            Assert.True(
                harness.PumpUntil(() => mate.Claims.Holder(DriverSlot) == ClientClaimsFreeHolder),
                "A player who quit to the menu left the lever welded to nobody.");
        }

        [Fact]
        public void AJoinerIsToldWhichSlotsAreAlreadyTaken() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient driver = harness.AddClient("Driver");
            HarnessClient latecomer = harness.AddClient("Latecomer");

            string joinCode = OpenOwnSession(harness, driver);
            driver.Claims.RequestClaim(DriverSlot);
            harness.PumpUntil(() => driver.Claims.IsHeldLocally(DriverSlot));

            JoinSession(harness, latecomer, joinCode);

            Assert.True(
                harness.PumpUntil(() => latecomer.Claims.Holder(DriverSlot) == driver.LocalPeerId),
                "The keyframe never told the newcomer the lever was taken.");
            Assert.False(latecomer.Claims.IsHeldLocally(DriverSlot));
        }

        [Fact]
        public void AGameSHandlersAreRegisteredOncePerProcess() {
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient first = harness.AddClient("First");
            HarnessClient second = harness.AddClient("Second");

            OpenOwnSession(harness, first);
            OpenOwnSession(harness, second);

            Assert.Equal(1, factory.RegisterHandlerCallCount);
            Assert.Equal(2, factory.Modules.Count);
        }

        [Fact]
        public void AGameSMessageReachesTheModuleOfTheSessionItsSenderIsIn() {
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient first = harness.AddClient("First");
            HarnessClient second = harness.AddClient("Second");

            OpenOwnSession(harness, first);
            OpenOwnSession(harness, second);

            second.Send(GameModuleTestMessage.MessageId, new GameModuleTestMessage(42));

            Assert.True(
                harness.PumpUntil(() => factory.Modules[1].Received.Count == 1),
                "The game's own message never reached its module.");
            Assert.Equal(42, factory.Modules[1].Received[0]);
            Assert.Empty(factory.Modules[0].Received);
        }

        [Fact]
        public void AMessageFromAPeerStillAtTheDeskResolvesToNoSession() {
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient wanderer = harness.AddClient("Wanderer");

            Assert.True(harness.Complete(wanderer.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            wanderer.Send(GameModuleTestMessage.MessageId, new GameModuleTestMessage(9));

            Assert.True(
                harness.PumpUntil(() => factory.UnroutedPayloads.Count == 1),
                "A message from a connection in no session was not seen at all.");
            Assert.Equal(9, factory.UnroutedPayloads[0]);
        }

        [Fact]
        public void EverySessionGetsItsOwnModuleSteppedEveryTick() {
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient first = harness.AddClient("First");
            HarnessClient second = harness.AddClient("Second");

            OpenOwnSession(harness, first);
            OpenOwnSession(harness, second);

            Assert.True(
                harness.PumpUntil(() => factory.Modules[0].TickCount > 3 && factory.Modules[1].TickCount > 3),
                "A module was never stepped.");
            Assert.True(factory.Modules[0].LastServerTick > 0);
        }

        [Fact]
        public void AModuleIsToldWhoArrivedAndWhoLeft() {
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Owner");
            HarnessClient guest = harness.AddClient("Guest");

            string joinCode = OpenOwnSession(harness, owner);
            JoinSession(harness, guest, joinCode);

            RecordingSessionModule module = factory.Modules[0];
            Assert.True(harness.PumpUntil(() => module.Joined.Count == 2), "The module was never told about both arrivals.");

            harness.Complete(guest.Session.LeaveAsync());

            Assert.True(harness.PumpUntil(() => module.Left.Count == 1), "The module was never told about the departure.");
            Assert.Equal(module.Joined[1], module.Left[0]);
        }

        [Fact]
        public void AModuleIsGivenTheEntryItBelongsTo() {
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Owner");
            OpenOwnSession(harness, owner);

            SessionEntry entry = harness.Query(() => harness.Registry.Sessions[0]);
            RecordingSessionModule module = factory.Modules[0];

            Assert.Same(entry, module.Entry);
            Assert.Same(module, entry.Module);
            Assert.NotNull(entry.Claims);
            Assert.NotNull(entry.Spawner);
            Assert.NotNull(entry.Replication);
        }

        [Fact]
        public void AModuleGoesDownWithTheSessionItBelongedTo() {
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Owner");
            OpenOwnSession(harness, owner);

            RecordingSessionModule module = factory.Modules[0];
            Assert.False(module.IsDisposed);

            harness.Stop();

            Assert.True(harness.PumpUntil(() => module.IsDisposed), "The module outlived its session.");
        }

        /// <summary>The value <c>ClientClaims</c> reports for a slot nobody holds.</summary>
        private const int ClientClaimsFreeHolder = -1;

        private static string OpenOwnSession(DedicatedServerHarness harness, HarnessClient client) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            SessionJoinResult created = harness.Complete(client.Session.CreateSessionAsync(string.Empty));
            Assert.True(created.IsSuccess);
            harness.PumpUntil(() => client.HasLocalPeerId);
            return created.JoinCode;
        }

        private static void JoinSession(DedicatedServerHarness harness, HarnessClient client, string joinCode) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            Assert.True(harness.Complete(client.Session.JoinSessionAsync(joinCode)).IsSuccess);
            harness.PumpUntil(() => client.HasLocalPeerId);
        }
    }
}
