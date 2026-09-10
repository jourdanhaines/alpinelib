using System.Threading.Tasks;
using AlpineLib.Chat;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.Hosting;
using AlpineLib.Server.Sessions;
using AlpineLib.Server.Sessions.Spawning;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Drives a real dedicated server end to end: the loop thread, the socket, the front desk, the
    /// sessions, the pawns, the slots and the chat pipeline, with clients that speak the shipping
    /// protocol.
    /// </summary>
    /// <remarks>
    /// Every join in this file goes the whole way — connect, authenticate, create or join by code — and
    /// the assertions are about what a player would notice: that a friend's code lets them in, that both
    /// of them get a body, that the two of them can talk, that leaving hands the connection back rather
    /// than dropping it, and that a server going down says so instead of vanishing. Those are the joins
    /// between the pieces, and they are the only places this stack has ever broken.
    /// </remarks>
    public sealed class ServerHostIntegrationTests {
        [Fact]
        public void AFriendJoinsTheSessionWithTheCodeItsOwnerWasGiven() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            Assert.True(created.IsSuccess);
            Assert.Equal(JoinCodeGenerator.CodeLength, created.JoinCode.Length);

            SessionJoinResult joined = JoinSession(harness, friend, created.JoinCode);
            Assert.True(joined.IsSuccess);
            Assert.False(joined.IsRejoin);
            Assert.Equal(created.SessionId, joined.SessionId);

            Assert.True(
                harness.PumpUntil(() => owner.Session.Members.Count == 2 && friend.Session.Members.Count == 2),
                "The roster never reached both clients.");

            Assert.True(owner.Session.IsOwner);
            Assert.False(friend.Session.IsOwner);
        }

        [Fact]
        public void EveryArrivalIsGivenABodyTheWholeSessionCanSee() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            JoinSession(harness, friend, created.JoinCode);

            Assert.True(
                harness.PumpUntil(() => owner.Replication.Entities.Count == 2 && friend.Replication.Entities.Count == 2),
                "Both pawns never reached both clients.");

            Assert.NotNull(owner.FindOwnPawn());
            Assert.NotNull(friend.FindOwnPawn());
            Assert.NotEqual(owner.FindOwnPawn().Id, friend.FindOwnPawn().Id);
        }

        [Fact]
        public void TheSpawnSectionDecidesWhatThePawnIsAndWhoSimulatesIt() {
            SpawnSettingsDocument spawn = new SpawnSettingsDocument {
                PawnPrefabId = 0,
                PawnAuthority = AuthorityMode.OwnerClient,
                Placement = SpawnPlacementKind.Ring,
                RingRadius = 4f,
                RingSeats = 2
            };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(spawn), new ServerRuntimeOptions(), null);
            HarnessClient owner = harness.AddClient("Driver");

            CreateSession(harness, owner);
            Assert.True(harness.PumpUntil(() => owner.FindOwnPawn() != null), "The owner never got a body.");

            Assert.Equal(AuthorityMode.OwnerClient, owner.FindOwnPawn().Authority);
        }

        [Fact]
        public void AuthoredSpawnPointsAreWhereThePlayersStand() {
            SpawnSettingsDocument spawn = new SpawnSettingsDocument {
                Placement = SpawnPlacementKind.List,
                Points = {
                    new SpawnPointDocument { X = 10f, Y = 0f, Z = -3f },
                    new SpawnPointDocument { X = -8f, Y = 0f, Z = 6f }
                }
            };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(spawn), new ServerRuntimeOptions(), null);
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            JoinSession(harness, friend, created.JoinCode);
            Assert.True(harness.PumpUntil(() => owner.FindOwnPawn() != null && friend.FindOwnPawn() != null),
                "Both pawns never arrived.");

            Assert.Equal(10f, owner.FindOwnPawn().State.Position.X, 3);
            Assert.Equal(-8f, friend.FindOwnPawn().State.Position.X, 3);
        }

        [Fact]
        public void APlayerWhoLeavesTakesTheirBodyWithThem() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            JoinSession(harness, friend, created.JoinCode);
            harness.PumpUntil(() => owner.Replication.Entities.Count == 2);

            harness.Complete(friend.Session.LeaveAsync());

            Assert.True(harness.PumpUntil(() => owner.Replication.Entities.Count == 1),
                "The departed player's body was left standing.");
        }

        [Fact]
        public void TheDirectoryReportsWhatTheServerIsHosting() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            JoinSession(harness, friend, created.JoinCode);
            harness.PumpUntil(() => owner.Session.Members.Count == 2);

            DirectorySnapshot directory = harness.CaptureDirectory();
            Assert.Single(directory.Sessions);

            SessionSnapshot session = directory.FindByJoinCode(created.JoinCode);
            Assert.NotNull(session);
            Assert.Equal(created.SessionId, session.SessionId);
            Assert.Equal(2, session.MemberCount);
            Assert.Equal(2, session.ConnectedMemberCount);
            Assert.Equal(SessionPhase.Lobby, session.Phase);
            Assert.Equal(owner.PlayerId, session.OwnerPlayerId);
            Assert.Equal(2, directory.ConnectedPeerCount);
        }

        [Fact]
        public void TwoPlayersInOneSessionTalkToEachOther() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            JoinSession(harness, friend, created.JoinCode);
            harness.PumpUntil(() => friend.Session.Members.Count == 2);

            ChatChannelId room = ChatChannelId.Room(created.SessionId);
            ConnectChat(harness, owner, created.SessionId);
            ConnectChat(harness, friend, created.SessionId);

            ChatSendResult sent = harness.Complete(owner.SendChatAsync(room, "coupling up"));
            Assert.True(sent.IsAccepted);
            Assert.Equal(1uL, sent.MessageId);

            Assert.True(harness.PumpUntil(() => friend.ChatMessages.Count >= 1), "The line never reached the other player.");
            Assert.Equal("coupling up", friend.ChatMessages[0].Text);
            Assert.Equal("Driver", friend.ChatMessages[0].SenderDisplayName);
            Assert.Equal(owner.PlayerId, friend.ChatMessages[0].SenderId);
        }

        [Fact]
        public void WhenTheOwnerQuitsTheSessionCarriesOnUnderANewOwner() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            JoinSession(harness, friend, created.JoinCode);
            harness.PumpUntil(() => friend.Session.Members.Count == 2);

            harness.Complete(owner.Session.LeaveAsync());

            Assert.True(harness.PumpUntil(() => friend.Session.IsOwner), "Ownership never moved to the remaining member.");

            SessionSnapshot session = harness.CaptureDirectory().FindBySessionId(created.SessionId);
            Assert.NotNull(session);
            Assert.Equal(1, session.ConnectedMemberCount);
            Assert.Equal(friend.PlayerId, session.OwnerPlayerId);
        }

        [Fact]
        public void TheProcessRefusesMoreSessionsThanItWasConfiguredToHost() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start(1);
            HarnessClient first = harness.AddClient("Driver");
            HarnessClient second = harness.AddClient("Fireman");

            Assert.True(CreateSession(harness, first).IsSuccess);

            harness.Complete(second.Session.ConnectAsync(harness.Endpoint));
            SessionJoinResult refused = harness.Complete(second.Session.CreateSessionAsync(string.Empty));

            Assert.False(refused.IsSuccess);
            Assert.Equal(SessionEndReason.Full, refused.Reason);
            Assert.Single(harness.CaptureDirectory().Sessions);
        }

        [Fact]
        public void AnUnknownCodeIsRefusedRatherThanIgnored() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient player = harness.AddClient("Driver");

            harness.Complete(player.Session.ConnectAsync(harness.Endpoint));
            SessionJoinResult refused = harness.Complete(player.Session.JoinSessionAsync("ZZZZZZ"));

            Assert.False(refused.IsSuccess);
            Assert.Equal(SessionEndReason.SessionNotFound, refused.Reason);
        }

        [Fact]
        public void ShuttingDownTellsEveryMemberWhyBeforeTheSocketGoes() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient friend = harness.AddClient("Fireman");

            SessionJoinResult created = CreateSession(harness, owner);
            JoinSession(harness, friend, created.JoinCode);
            harness.PumpUntil(() => friend.Session.Members.Count == 2);

            harness.Stop();

            Assert.True(
                harness.PumpUntil(() => owner.SessionEndings.Count > 0 && friend.SessionEndings.Count > 0),
                "The shutdown notice never reached the members.");

            Assert.Equal(SessionEndReason.HostClosed, owner.SessionEndings[0]);
            Assert.Equal(SessionEndReason.HostClosed, friend.SessionEndings[0]);
        }

        private static SessionJoinResult CreateSession(DedicatedServerHarness harness, HarnessClient client) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            SessionJoinResult created = harness.Complete(client.Session.CreateSessionAsync(string.Empty));
            harness.PumpUntil(() => client.HasLocalPeerId);
            return created;
        }

        private static SessionJoinResult JoinSession(DedicatedServerHarness harness, HarnessClient client, string joinCode) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            SessionJoinResult joined = harness.Complete(client.Session.JoinSessionAsync(joinCode));
            harness.PumpUntil(() => client.HasLocalPeerId);
            return joined;
        }

        private static void ConnectChat(DedicatedServerHarness harness, HarnessClient client, string roomKey) {
            Task connecting = client.ConnectChatAsync(roomKey);
            Assert.True(harness.PumpUntil(() => connecting.IsCompleted), "The chat provider never connected.");
        }
    }
}
