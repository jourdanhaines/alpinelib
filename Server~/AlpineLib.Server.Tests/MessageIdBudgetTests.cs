using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AlpineLib.Chat.Wire;
using AlpineLib.Netcode.Protocol;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The budget checked against the id constants it claims to cover. The bands are written out by hand
    /// so that a retired id stays reserved, which is exactly the property that lets them drift out of
    /// step with reality — so the test discovers the id owners rather than naming them: a list of types
    /// would need the same maintenance the budget is trying to avoid, and a band added by a later work
    /// package would sit uncovered until someone noticed.
    /// </summary>
    public sealed class MessageIdBudgetTests {
        [Fact]
        public void EveryIdTheLibrarySpeaksIsReserved() {
            IReadOnlyList<IdConstant> ids = LibraryMessageIds();

            Assert.NotEmpty(ids);
            Assert.All(ids, AssertReserved);
        }

        [Fact]
        public void EveryIdOwnerInBothAssembliesIsFoundByTheScan() {
            IReadOnlyList<Type> owners = MessageIdOwners();

            // Names rather than count, so a new owner is a failure that says which one is missing from
            // the list a reader expects — not a number nobody can interpret.
            Assert.Contains(owners, owner => owner.Name == "CoreMessageIds");
            Assert.Contains(owners, owner => owner.Name == "SessionMessageIds");
            Assert.Contains(owners, owner => owner.Name == "ClaimMessageIds");
            Assert.Contains(owners, owner => owner.Name == "ReplicationMessageIds");
            Assert.Contains(owners, owner => owner.Name == "ChatMessageIds");
        }

        [Fact]
        public void TheWholeClaimBandIsReserved() {
            Assert.True(MessageIdBudget.IsReservedByLibrary(MessageIdBudget.ClaimBandStart));
            Assert.True(MessageIdBudget.IsReservedByLibrary(85));
            Assert.True(MessageIdBudget.IsReservedByLibrary(87));
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
            Assert.False(MessageIdBudget.IsReservedByLibrary(88));
            Assert.False(MessageIdBudget.IsReservedByLibrary(119));
        }

        [Fact]
        public void TheChatEnvelopeIsReservedEvenThoughItLivesInAnotherAssembly() {
            Assert.Equal(ChatMessageIds.ChatPayload, MessageIdBudget.ChatEnvelopeId);
            Assert.True(MessageIdBudget.IsReservedByLibrary(ChatMessageIds.ChatPayload));
        }

        [Fact]
        public void TheGuardRefusesALibraryIdAndPassesAFreeOne() {
            ArgumentException error = Assert.Throws<ArgumentException>(
                () => MessageIdBudget.GuardGameMessageId(MessageIdBudget.ReplicationBandStart, "messageId"));

            Assert.Equal("messageId", error.ParamName);
            Assert.Contains("136-191", error.Message);

            MessageIdBudget.GuardGameMessageId(MessageIdBudget.GameBandStart, "messageId");
            MessageIdBudget.GuardGameMessageId(3, "messageId");
        }

        private static void AssertReserved(IdConstant id) {
            Assert.True(
                MessageIdBudget.IsReservedByLibrary(id.Value),
                id.Owner + "." + id.Name + " = " + id.Value.ToString(CultureInfo.InvariantCulture)
                + " is spoken by the library but the budget reports it as free.");
        }

        /// <summary>
        /// Every type in the library that owns a block of the id map, found by shape rather than named:
        /// a static class whose name ends in <c>MessageIds</c>.
        /// </summary>
        private static IReadOnlyList<Type> MessageIdOwners() {
            var assemblies = new[] {
                typeof(MessageIdBudget).Assembly,
                typeof(ChatMessageIds).Assembly,
            };

            var owners = new List<Type>();

            for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++) {
                CollectOwners(assemblies[assemblyIndex], owners);
            }

            return owners;
        }

        private static void CollectOwners(Assembly assembly, List<Type> owners) {
            Type[] types = assembly.GetTypes();

            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++) {
                Type type = types[typeIndex];

                if (IsMessageIdOwner(type)) {
                    owners.Add(type);
                }
            }
        }

        /// <summary>A static class — abstract and sealed to the runtime — named after the map it owns.</summary>
        private static bool IsMessageIdOwner(Type type) {
            return type.IsClass && type.IsAbstract && type.IsSealed && type.Name.EndsWith("MessageIds", StringComparison.Ordinal);
        }

        /// <summary>Every <c>const ushort</c> on the types that own a band of the id map.</summary>
        private static IReadOnlyList<IdConstant> LibraryMessageIds() {
            IReadOnlyList<Type> owners = MessageIdOwners();
            var ids = new List<IdConstant>();

            for (int ownerIndex = 0; ownerIndex < owners.Count; ownerIndex++) {
                CollectIds(owners[ownerIndex], ids);
            }

            return ids;
        }

        private static void CollectIds(Type idOwner, List<IdConstant> ids) {
            FieldInfo[] fields = idOwner.GetFields(BindingFlags.Public | BindingFlags.Static);

            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++) {
                FieldInfo field = fields[fieldIndex];

                if (!field.IsLiteral || field.FieldType != typeof(ushort)) {
                    continue;
                }

                ids.Add(new IdConstant(idOwner.Name, field.Name, (ushort)field.GetRawConstantValue()));
            }
        }

        /// <summary>One discovered id constant, carrying enough to name itself in a failure.</summary>
        private readonly struct IdConstant {
            public IdConstant(string owner, string name, ushort value) {
                Owner = owner;
                Name = name;
                Value = value;
            }

            /// <summary>The class that declares it.</summary>
            public string Owner { get; }

            /// <summary>The constant's own name.</summary>
            public string Name { get; }

            /// <summary>The wire id.</summary>
            public ushort Value { get; }
        }
    }
}
