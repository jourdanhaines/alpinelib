using AlpineLib.Netcode.Appearance;
using AlpineLib.Netcode.Sessions;
using Xunit;
using static AlpineLib.Server.Tests.AppearanceTestCatalog;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Join-time avatar data to model id, and peer id to roster member.
    /// </summary>
    public sealed class AppearanceModelResolverTests {
        private readonly AppearanceCatalogTable catalog = Build();

        [Fact]
        public void AKnownModelIsKept() {
            Assert.Equal(ModelB, AppearanceModelResolver.FromAvatarData(ModelB, catalog));
        }

        [Fact]
        public void ZeroFallsBackToTheDefault() {
            Assert.Equal(ModelA, AppearanceModelResolver.FromAvatarData(0, catalog));
        }

        [Fact]
        public void AnUnknownModelFallsBackToTheDefault() {
            Assert.Equal(ModelA, AppearanceModelResolver.FromAvatarData(999, catalog));
        }

        [Fact]
        public void TheConnectedMemberOnAPeerIsFound() {
            var reservation = new SessionMember { PlayerId = PlayerId.None, IsConnected = false };
            var other = new SessionMember(2, PlayerId.None, "Other", false, 0);
            var target = new SessionMember(5, PlayerId.None, "Target", false, 0);

            Assert.True(AppearanceModelResolver.TryFindMember(new[] { reservation, other, target }, 5, out SessionMember found));
            Assert.Same(target, found);
        }

        [Fact]
        public void NoMemberMatchesAnAbsentPeer() {
            var member = new SessionMember(2, PlayerId.None, "Other", false, 0);

            Assert.False(AppearanceModelResolver.TryFindMember(new[] { member }, 3, out SessionMember found));
            Assert.Null(found);
        }

        [Fact]
        public void AReservationNeverMatches() {
            var reservation = new SessionMember { IsConnected = false };

            Assert.False(AppearanceModelResolver.TryFindMember(new[] { reservation }, SessionMember.NoPeerId, out _));
        }

        [Fact]
        public void ADisconnectedMemberStillHoldingItsPeerIdDoesNotMatch() {
            var stale = new SessionMember(4, PlayerId.None, "Stale", false, 0) { IsConnected = false };

            Assert.False(AppearanceModelResolver.TryFindMember(new[] { stale }, 4, out _));
        }
    }
}
