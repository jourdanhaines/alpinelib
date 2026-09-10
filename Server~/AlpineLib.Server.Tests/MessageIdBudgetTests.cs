using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AlpineLib.Chat.Wire;
using AlpineLib.Netcode.Messages;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Sessions.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The budget checked against the id constants it claims to cover. The bands are written out by hand
    /// so that a retired id stays reserved, which is exactly the property that lets them drift out of
    /// step with reality — so the test walks the real constants by reflection rather than restating a
    /// list that would need the same maintenance the budget is trying to avoid.
    /// </summary>
    public sealed class MessageIdBudgetTests {
        [Fact]
        public void EveryIdTheLibrarySpeaksIsReserved() {
            IReadOnlyList<ushort> ids = LibraryMessageIds();

            Assert.NotEmpty(ids);
            Assert.All(ids, AssertReserved);
        }

        [Fact]
        public void TheWholeClaimBandIsReserved() {
            Assert.True(MessageIdBudget.IsReservedByLibrary(MessageIdBudget.ClaimBandStart));
            Assert.True(MessageIdBudget.IsReservedByLibrary(85));
            Assert.True(MessageIdBudget.IsReservedByLibrary(MessageIdBudget.ClaimBandEnd));
        }

        [Fact]
        public void TheSessionTailStaysReservedForListenHostMigration() {
            Assert.True(MessageIdBudget.IsReservedByLibrary(120));
            Assert.True(MessageIdBudget.IsReservedByLibrary(127));
        }

        [Fact]
        public void TheGameBandIsFreeAndBoundedWhereTheIdMapSaysItIs() {
            Assert.Equal(136, MessageIdBudget.GameBandStart);
            Assert.Equal(191, MessageIdBudget.GameBandEnd);

            Assert.False(MessageIdBudget.IsReservedByLibrary(136));
            Assert.False(MessageIdBudget.IsReservedByLibrary(191));
            Assert.True(MessageIdBudget.IsInGameBand(136));
            Assert.True(MessageIdBudget.IsInGameBand(191));

            Assert.False(MessageIdBudget.IsInGameBand(135));
            Assert.False(MessageIdBudget.IsInGameBand(192));
        }

        [Fact]
        public void TheGapsBetweenLibraryBandsAreFreeToo() {
            Assert.False(MessageIdBudget.IsReservedByLibrary(3));
            Assert.False(MessageIdBudget.IsReservedByLibrary(63));
            Assert.False(MessageIdBudget.IsReservedByLibrary(87));
            Assert.False(MessageIdBudget.IsReservedByLibrary(119));
        }

        [Fact]
        public void TheChatEnvelopeIsReservedEvenThoughItLivesInAnotherAssembly() {
            Assert.Equal(ChatMessageIds.ChatPayload, MessageIdBudget.ChatEnvelopeId);
            Assert.True(MessageIdBudget.IsReservedByLibrary(ChatMessageIds.ChatPayload));
        }

        private static void AssertReserved(ushort id) {
            Assert.True(
                MessageIdBudget.IsReservedByLibrary(id),
                "Message id " + id.ToString(CultureInfo.InvariantCulture)
                + " is spoken by the library but the budget reports it as free.");
        }

        /// <summary>Every <c>const ushort</c> on the types that own a band of the id map.</summary>
        private static IReadOnlyList<ushort> LibraryMessageIds() {
            var idOwners = new[] {
                typeof(CoreMessageIds),
                typeof(SessionMessageIds),
                typeof(ClaimMessageIds),
                typeof(ReplicationMessageIds),
                typeof(ChatMessageIds),
            };

            var ids = new List<ushort>();

            for (int ownerIndex = 0; ownerIndex < idOwners.Length; ownerIndex++) {
                CollectIds(idOwners[ownerIndex], ids);
            }

            return ids;
        }

        private static void CollectIds(Type idOwner, List<ushort> ids) {
            FieldInfo[] fields = idOwner.GetFields(BindingFlags.Public | BindingFlags.Static);

            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++) {
                FieldInfo field = fields[fieldIndex];

                if (!field.IsLiteral || field.FieldType != typeof(ushort)) {
                    continue;
                }

                ids.Add((ushort)field.GetRawConstantValue());
            }
        }
    }
}
