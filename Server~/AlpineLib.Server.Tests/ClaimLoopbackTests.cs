using AlpineLib.Netcode.Sessions.Claims;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The claim registry and two real clients driven against each other over an in-memory transport,
    /// one pump at a time. These are the tests that catch what the server-side rules cannot see: a
    /// verdict that never goes out, a grant raised on the wrong client, a slot still held by somebody who
    /// has already gone.
    /// </summary>
    public sealed class ClaimLoopbackTests {
        private const ushort DriverLever = 4;
        private const ushort BrakeLever = 5;

        [Fact]
        public void ARequestGrantsTheSlotAndTheWholeSessionIsTold() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();
            ClaimLoopbackClient passenger = world.ConnectClient();
            ClearLogs(driver, passenger);

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);

            Assert.Equal(driver.ServerSidePeer.Id, driver.Claims.Holder(DriverLever));
            Assert.Equal(driver.ServerSidePeer.Id, passenger.Claims.Holder(DriverLever));
            Assert.True(driver.Claims.IsHeldLocally(DriverLever));
            Assert.False(passenger.Claims.IsHeldLocally(DriverLever));
            Assert.True(passenger.Claims.IsHeldBy(DriverLever, driver.ServerSidePeer.Id));
        }

        [Fact]
        public void OnlyTheNewHolderIsToldItWasGranted() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();
            ClaimLoopbackClient passenger = world.ConnectClient();
            ClearLogs(driver, passenger);

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);

            Assert.Equal(new[] { DriverLever }, driver.Granted);
            Assert.Empty(driver.Lost);
            Assert.Empty(passenger.Granted);
            Assert.Empty(passenger.Lost);
            Assert.Single(passenger.Verdicts);
        }

        [Fact]
        public void ASecondRequesterIsRefusedAndNobodyHearsAboutIt() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();
            ClaimLoopbackClient rival = world.ConnectClient();

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);
            ClearLogs(driver, rival);

            rival.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.Equal(driver.ServerSidePeer.Id, rival.Claims.Holder(DriverLever));
            Assert.Empty(rival.Verdicts);
            Assert.Empty(rival.Granted);
            Assert.Empty(driver.Verdicts);
            Assert.Empty(driver.Lost);
        }

        [Fact]
        public void RepeatingARequestYouAlreadyWonSaysNothingTwice() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();
            ClaimLoopbackClient passenger = world.ConnectClient();

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);
            ClearLogs(driver, passenger);

            driver.Claims.RequestClaim(DriverLever);
            driver.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.Empty(driver.Verdicts);
            Assert.Empty(driver.Granted);
            Assert.Empty(passenger.Verdicts);
        }

        [Fact]
        public void TheHolderReleasingFreesTheSlotForEverybody() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();
            ClaimLoopbackClient passenger = world.ConnectClient();

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);
            ClearLogs(driver, passenger);

            driver.Claims.Release(DriverLever);
            world.Pump(2);

            Assert.Equal(ClientClaims.FreeHolderPeerId, driver.Claims.Holder(DriverLever));
            Assert.Equal(ClientClaims.FreeHolderPeerId, passenger.Claims.Holder(DriverLever));
            Assert.Equal(new[] { DriverLever }, driver.Lost);
            Assert.Empty(passenger.Lost);
            Assert.Single(passenger.Verdicts);
        }

        [Fact]
        public void ANonHolderCannotReleaseTheSlotUnderneathTheHolder() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();
            ClaimLoopbackClient rival = world.ConnectClient();

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);
            ClearLogs(driver, rival);

            rival.Claims.Release(DriverLever);
            world.Pump(4);

            Assert.True(driver.Claims.IsHeldLocally(DriverLever));
            Assert.Empty(driver.Lost);
            Assert.Empty(rival.Verdicts);
        }

        /// <summary>
        /// The case the whole feature turns on. A player who drops still holds the lever as far as the
        /// registry is concerned, and nobody but the holder may release it, so without the retire path a
        /// disconnect would strand the slot for the rest of the session.
        /// </summary>
        [Fact]
        public void ADisconnectFreesEverySlotTheDepartedHeld() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient leaving = world.ConnectClient();
            ClaimLoopbackClient staying = world.ConnectClient();

            leaving.Claims.RequestClaim(DriverLever);
            leaving.Claims.RequestClaim(BrakeLever);
            world.Pump(2);
            ClearLogs(leaving, staying);

            world.DropClient(leaving);
            world.Pump(4);

            Assert.Empty(world.Registry.Holders);
            Assert.Equal(ClientClaims.FreeHolderPeerId, staying.Claims.Holder(DriverLever));
            Assert.Equal(ClientClaims.FreeHolderPeerId, staying.Claims.Holder(BrakeLever));
            Assert.Equal(2, staying.Verdicts.Count);
        }

        [Fact]
        public void ASlotFreedByADisconnectCanBeTakenByWhoeverIsLeft() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient leaving = world.ConnectClient();
            ClaimLoopbackClient staying = world.ConnectClient();

            leaving.Claims.RequestClaim(DriverLever);
            world.Pump(2);
            world.DropClient(leaving);
            world.Pump(4);
            staying.ClearLog();

            staying.Claims.RequestClaim(DriverLever);
            world.Pump(2);

            Assert.True(staying.Claims.IsHeldLocally(DriverLever));
            Assert.Equal(new[] { DriverLever }, staying.Granted);
        }

        /// <summary>
        /// A joiner has to be told what is already held, and must not read that keyframe as a grant of its
        /// own — the slots in it belong to people who were here first.
        /// </summary>
        [Fact]
        public void AJoinerLearnsTheHeldSlotsFromTheKeyframeWithoutBeingGrantedThem() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);

            ClaimLoopbackClient joiner = world.ConnectClient();
            world.Registry.SendKeyframeTo(joiner.ServerSidePeer);
            world.Pump(2);

            Assert.Equal(driver.ServerSidePeer.Id, joiner.Claims.Holder(DriverLever));
            Assert.Empty(joiner.Granted);
            Assert.Empty(joiner.Lost);
        }

        [Fact]
        public void AKeyframeThatRepeatsWhatAClientAlreadyKnowsRaisesNothing() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);
            driver.ClearLog();

            world.Registry.SendKeyframeTo(driver.ServerSidePeer);
            world.Pump(2);

            Assert.True(driver.Claims.IsHeldLocally(DriverLever));
            Assert.Empty(driver.Verdicts);
            Assert.Empty(driver.Granted);
        }

        /// <summary>
        /// A request from a connection that is not a member of this session is dropped rather than
        /// answered — the sender is left seeing exactly the state everybody else does.
        /// </summary>
        [Fact]
        public void ARequestFromAConnectionOutsideTheSessionIsIgnored() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient outsider = world.ConnectClient();
            world.SessionPeers.Remove(outsider.ServerSidePeer);
            outsider.ClearLog();

            outsider.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            Assert.False(world.Registry.IsHeld(DriverLever));
            Assert.Empty(outsider.Verdicts);
        }

        /// <summary>
        /// Leaving a session drops the view without pretending the slots were released: the events belong
        /// to a session that no longer exists, and the client is about to be disposed anyway.
        /// </summary>
        [Fact]
        public void ClearForgetsEverySlotWithoutRaisingALoss() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(2);
            driver.ClearLog();

            driver.Claims.Clear();

            Assert.False(driver.Claims.IsHeldLocally(DriverLever));
            Assert.Empty(driver.Claims.Holders);
            Assert.Empty(driver.Lost);
            Assert.Empty(driver.Verdicts);
        }

        /// <summary>
        /// A verdict that lands before the roster does is still recorded, so the peer id arriving late
        /// cannot leave a client believing a slot it holds is somebody else's.
        /// </summary>
        [Fact]
        public void AVerdictReceivedBeforeThePeerIdIsAdoptedStillCounts() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();
            driver.Claims.LocalPeerId = ClientClaims.FreeHolderPeerId;

            world.Registry.TryClaim(DriverLever, driver.ServerSidePeer);
            world.Pump(2);

            Assert.False(driver.Claims.IsHeldLocally(DriverLever));
            Assert.Empty(driver.Granted);

            driver.Claims.LocalPeerId = driver.ServerSidePeer.Id;

            Assert.True(driver.Claims.IsHeldLocally(DriverLever));
        }

        private static void ClearLogs(params ClaimLoopbackClient[] clients) {
            for (int clientIndex = 0; clientIndex < clients.Length; clientIndex++) {
                clients[clientIndex].ClearLog();
            }
        }
    }
}
