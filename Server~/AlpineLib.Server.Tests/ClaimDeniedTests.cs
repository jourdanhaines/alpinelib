using System.Collections.Generic;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What a client hears back when it asks for a slot and does not get it.
    /// </summary>
    /// <remarks>
    /// The old contract was silence, which forced every consumer to carry a timer: a lever the player
    /// reached for stayed "pending" for two seconds before the game gave up on it. These pin the three
    /// shapes an answer now takes, and the one case that is still deliberately silent.
    /// </remarks>
    public sealed class ClaimDeniedTests {
        private const ushort DriverLever = 4;

        /// <summary>A slot somebody else holds refuses the asker by name, without moving the slot.</summary>
        [Fact]
        public void ARequestForASlotSomebodyElseHoldsIsRefusedToTheAskerAlone() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient holder = world.ConnectClient();
            ClaimLoopbackClient loser = world.ConnectClient();

            world.Registry.TryClaim(DriverLever, holder.ServerSidePeer);
            world.Pump(4);

            holder.ClearLog();
            loser.ClearLog();

            loser.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.Equal(new ushort[] { DriverLever }, loser.Denied);
            Assert.Empty(loser.Verdicts);
            Assert.Empty(loser.Granted);

            // The holder hears nothing at all: a refusal does not move the slot and is nobody else's
            // business.
            Assert.Empty(holder.Denied);
            Assert.Empty(holder.Verdicts);
            Assert.Equal(holder.ServerSidePeer.Id, world.Registry.Holder(DriverLever));
        }

        /// <summary>
        /// The holder re-asking for its own slot is answered with the verdict it already agrees with,
        /// rather than with silence or with a refusal.
        /// </summary>
        [Fact]
        public void AHolderThatAsksAgainIsAnsweredRatherThanIgnored() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient holder = world.ConnectClient();
            ClaimWireSpy bystander = world.ConnectSpy();

            holder.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.Equal(new ushort[] { DriverLever }, holder.Granted);

            holder.ClearLog();
            bystander.ClearLog();

            holder.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.Empty(holder.Denied);

            // The view drops a verdict that agrees with what it holds, so the client raises nothing —
            // but the reply did arrive, which is what the bystander's silence proves it was a unicast.
            Assert.Empty(holder.Verdicts);
            Assert.Empty(bystander.Received);
            Assert.Equal(holder.ServerSidePeer.Id, world.Registry.Holder(DriverLever));
        }

        /// <summary>A slot the game's own rule refuses is refused to the asker, not broadcast.</summary>
        [Fact]
        public void ASlotTheValidatorRefusesAnswersTheAskerWithARefusal() {
            using var world = new ClaimLoopbackWorld(isClaimAllowed: (slot, requester, claims) => slot < 4);
            ClaimLoopbackClient player = world.ConnectClient();
            ClaimWireSpy bystander = world.ConnectSpy();

            player.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.Equal(new ushort[] { DriverLever }, player.Denied);
            Assert.False(world.Registry.IsHeld(DriverLever));
            Assert.Empty(bystander.Received);
        }

        /// <summary>
        /// The validator is handed the requester and the registry, so a rule about sibling slots — one
        /// driver per train, whichever of its levers they took — is expressible.
        /// </summary>
        [Fact]
        public void AValidatorCanRefuseASecondPeerEveryLeverOnATrainTheFirstIsDriving() {
            using var world = new ClaimLoopbackWorld(isClaimAllowed: OnlyOneDriverPerTrain);
            ClaimLoopbackClient driver = world.ConnectClient();
            ClaimLoopbackClient passenger = world.ConnectClient();

            driver.Claims.RequestClaim(10);
            world.Pump(4);

            Assert.Equal(new ushort[] { 10 }, driver.Granted);

            // A different lever on the same train, asked for by somebody else.
            passenger.Claims.RequestClaim(11);
            world.Pump(4);

            Assert.Equal(new ushort[] { 11 }, passenger.Denied);
            Assert.Empty(passenger.Granted);
            Assert.False(world.Registry.IsHeld(11));

            // The driver may still take a second lever on the train it is already driving.
            driver.Claims.RequestClaim(11);
            world.Pump(4);

            Assert.Equal(new ushort[] { 10, 11 }, driver.Granted);
        }

        /// <summary>A request from a peer this session has never heard of is still answered with nothing.</summary>
        [Fact]
        public void ARequestFromOutsideTheSessionIsAnsweredWithNothing() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient outsider = world.ConnectClient();

            world.SessionPeers.Remove(outsider.ServerSidePeer);

            outsider.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.Empty(outsider.Denied);
            Assert.Empty(outsider.Verdicts);
            Assert.False(world.Registry.IsHeld(DriverLever));
        }

        /// <summary>A request past the slot cap refuses the asker rather than dropping the message.</summary>
        [Fact]
        public void ARequestPastTheSlotCapIsRefusedToTheAsker() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient player = world.ConnectClient();

            FillTheSlotMap(world, player.ServerSidePeer);
            world.Pump(4);
            player.ClearLog();

            player.Claims.RequestClaim(ServerClaimRegistry.MaxSlots + 1);
            world.Pump(4);

            Assert.Equal(new ushort[] { ServerClaimRegistry.MaxSlots + 1 }, player.Denied);
        }

        /// <summary>Seats every slot the cap allows, so the next number asked for is one too many.</summary>
        private static void FillTheSlotMap(ClaimLoopbackWorld world, PeerHandle holder) {
            for (int slot = 0; slot < ServerClaimRegistry.MaxSlots; slot++) {
                world.Registry.TryClaim((ushort)slot, holder);
            }
        }

        /// <summary>
        /// One driver per train, where a train is a band of ten slots. Refuses a peer any lever on a
        /// train another peer already holds a lever on.
        /// </summary>
        private static bool OnlyOneDriverPerTrain(ushort slot, PeerHandle requester, ServerClaimRegistry claims) {
            int train = slot / 10;

            foreach (KeyValuePair<ushort, int> held in claims.Holders) {
                if (held.Key / 10 != train) continue;
                if (held.Value == requester.Id) continue;

                return false;
            }

            return true;
        }
    }
}
