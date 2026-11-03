using System;
using AlpineLib.Netcode.Appearance;
using AlpineLib.Server.Configuration;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Required slots: a model may name slots every outfit must fill (a face's nose). The rules refuse an
    /// outfit leaving one empty, the table refuses a required slot the model lacks, and the JSON carries
    /// <c>requiredSlots</c> only for models that have any, so catalogs without them are unchanged.
    /// </summary>
    public sealed class AppearanceRequiredSlotTests {
        private const ushort Face = 1;
        private const ushort Plain = 2;
        private const ushort NoseForFace = 20;
        private const ushort HatForAny = 21;
        private const int NoseSlot = 2;

        private static AppearanceCatalogTable Build() {
            return new AppearanceCatalogTable(
                Face,
                new[] {
                    new AppearanceModelInfo(Face, 3, 0, AppearanceModelInfo.MaskOf(new[] { NoseSlot })),
                    new AppearanceModelInfo(Plain, 3, 0),
                },
                new[] {
                    new AppearanceItemInfo(NoseForFace, NoseSlot, 1, new[] { Face }),
                    new AppearanceItemInfo(HatForAny, 0, 1, null),
                });
        }

        [Fact]
        public void AnOutfitFillingTheRequiredSlotPasses() {
            AppearanceOutfit outfit = AppearanceOutfit.EmptyFor(Face, 3).With(NoseSlot, new AppearanceSlotPick(NoseForFace, 0));

            Assert.True(AppearanceRules.Validate(outfit, Build(), out string error), error);
        }

        [Fact]
        public void AnOutfitLeavingTheRequiredSlotEmptyIsRefused() {
            AppearanceOutfit outfit = AppearanceOutfit.EmptyFor(Face, 3).With(0, new AppearanceSlotPick(HatForAny, 0));

            Assert.False(AppearanceRules.Validate(outfit, Build(), out string error));
            Assert.Contains("required", error);
            Assert.False(AppearanceRules.ValidateForModel(outfit, Face, Build(), out _));
        }

        [Fact]
        public void AModelWithoutRequiredSlotsStillTakesAnEmptyOutfit() {
            Assert.True(AppearanceRules.Validate(AppearanceOutfit.EmptyFor(Plain, 3), Build(), out string error), error);
        }

        [Fact]
        public void TheMaskNamesItsSlots() {
            var model = new AppearanceModelInfo(1, 12, 0, AppearanceModelInfo.MaskOf(new[] { 11, 0 }));

            Assert.True(model.IsSlotRequired(0));
            Assert.True(model.IsSlotRequired(11));
            Assert.False(model.IsSlotRequired(1));
            Assert.False(model.IsSlotRequired(-1));
            Assert.Equal(new byte[] { 0, 11 }, model.RequiredSlots());
            Assert.Throws<ArgumentException>(() => AppearanceModelInfo.MaskOf(new[] { AppearanceOutfit.MaxSlots }));
        }

        [Fact]
        public void ARequiredSlotPastTheSlotCountIsRefused() {
            var model = new AppearanceModelInfo(1, 2, 0, AppearanceModelInfo.MaskOf(new[] { 2 }));

            Assert.Throws<ArgumentException>(() => new AppearanceCatalogTable(1, new[] { model }, Array.Empty<AppearanceItemInfo>()));
        }

        [Fact]
        public void TheJsonCarriesRequiredSlotsOnlyWhereSet() {
            string json = AppearanceCatalogJsonWriter.Write(Build());

            Assert.Contains("{ \"id\": 1, \"slotCount\": 3, \"pawnPrefabId\": 0, \"requiredSlots\": [2] },", json);
            Assert.Contains("{ \"id\": 2, \"slotCount\": 3, \"pawnPrefabId\": 0 }\n", json);
        }

        [Fact]
        public void RequiredSlotsRoundTripThroughTheLoader() {
            AppearanceCatalogTable read = AppearanceCatalogLoader.Parse(AppearanceCatalogJsonWriter.Write(Build()), "required");

            Assert.True(read.TryGetModel(Face, out AppearanceModelInfo face));
            Assert.Equal(new byte[] { NoseSlot }, face.RequiredSlots());
            Assert.True(read.TryGetModel(Plain, out AppearanceModelInfo plain));
            Assert.Equal(0, plain.RequiredSlotMask);
            Assert.Equal(AppearanceCatalogJsonWriter.Write(Build()), AppearanceCatalogJsonWriter.Write(read));
        }

        [Theory]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 2, \"requiredSlots\": [2] } ] }")]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 2, \"requiredSlots\": [16] } ] }")]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 2, \"requiredSlots\": [-1] } ] }")]
        public void ABadRequiredSlotStopsTheLoader(string json) {
            Assert.Throws<InvalidOperationException>(() => AppearanceCatalogLoader.Parse(json, "required"));
        }
    }
}
