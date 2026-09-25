using System;
using AlpineLib.Netcode.Appearance;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The guards that keep an <see cref="AppearanceCatalogTable"/> one every outfit can be checked
    /// against, and the id ordering its consumers rely on.
    /// </summary>
    public sealed class AppearanceCatalogTableTests {
        private static readonly AppearanceModelInfo[] OneModel = { new AppearanceModelInfo(1, 4, 0) };

        [Fact]
        public void RowsAreSortedByIdAndLookUpByIdWorks() {
            var table = new AppearanceCatalogTable(
                2,
                new[] { new AppearanceModelInfo(2, 1, 0), new AppearanceModelInfo(1, 4, 5) },
                new[] { Item(9, 0), Item(3, 0), Item(5, 0) });

            Assert.Equal(new ushort[] { 1, 2 }, new[] { table.Models[0].Id, table.Models[1].Id });
            Assert.Equal(new ushort[] { 3, 5, 9 }, new[] { table.Items[0].Id, table.Items[1].Id, table.Items[2].Id });
            Assert.Equal(2, table.DefaultModelId);
            Assert.True(table.TryGetModel(1, out AppearanceModelInfo model));
            Assert.Equal(5, model.PawnPrefabId);
            Assert.True(table.TryGetItem(5, out AppearanceItemInfo item));
            Assert.Equal(5, item.Id);
            Assert.False(table.TryGetModel(3, out _));
            Assert.False(table.TryGetItem(0, out _));
        }

        [Fact]
        public void AnEmptyItemListIsFine() {
            var table = new AppearanceCatalogTable(1, OneModel, Array.Empty<AppearanceItemInfo>());

            Assert.Empty(table.Items);
        }

        [Fact]
        public void ModelIdZeroIsRefused() {
            AssertRefused(1, new[] { new AppearanceModelInfo(0, 4, 0), new AppearanceModelInfo(1, 4, 0) });
        }

        [Fact]
        public void ADuplicateModelIdIsRefused() {
            AssertRefused(1, new[] { new AppearanceModelInfo(1, 4, 0), new AppearanceModelInfo(1, 2, 0) });
        }

        [Fact]
        public void AModelWithNoSlotsIsRefused() {
            AssertRefused(1, new[] { new AppearanceModelInfo(1, 0, 0) });
        }

        [Fact]
        public void AModelBeyondTheSlotCapIsRefused() {
            AssertRefused(1, new[] { new AppearanceModelInfo(1, AppearanceOutfit.MaxSlots + 1, 0) });
            Assert.NotNull(new AppearanceCatalogTable(1, new[] { new AppearanceModelInfo(1, AppearanceOutfit.MaxSlots, 0) }, Array.Empty<AppearanceItemInfo>()));
        }

        [Fact]
        public void AnUnknownDefaultModelIsRefused() {
            AssertRefused(2, OneModel);
        }

        [Fact]
        public void ItemIdZeroIsRefused() {
            AssertRefused(1, OneModel, Item(0, 0));
        }

        [Fact]
        public void ADuplicateItemIdIsRefused() {
            AssertRefused(1, OneModel, Item(4, 0), Item(4, 1));
        }

        [Fact]
        public void AnItemWithNoVariantsIsRefused() {
            AssertRefused(1, OneModel, new AppearanceItemInfo(4, 0, 0, null));
        }

        [Fact]
        public void AnItemAllowingAnUnknownModelIsRefused() {
            AssertRefused(1, OneModel, new AppearanceItemInfo(4, 0, 1, new ushort[] { 1, 2 }));
        }

        [Fact]
        public void AnItemInASlotItsModelLacksIsRefused() {
            AssertRefused(1, OneModel, new AppearanceItemInfo(4, 4, 1, new ushort[] { 1 }));
        }

        [Fact]
        public void AnItemBeyondTheSlotCapIsRefused() {
            AssertRefused(1, OneModel, Item(4, AppearanceOutfit.MaxSlots));
        }

        [Fact]
        public void ANullItemIsRefused() {
            AssertRefused(1, OneModel, (AppearanceItemInfo)null);
        }

        [Fact]
        public void AllowedModelIdsAreSortedAndDeduplicated() {
            var item = new AppearanceItemInfo(4, 0, 1, new ushort[] { 3, 1, 3, 2 });

            Assert.Equal(new ushort[] { 1, 2, 3 }, item.AllowedModelIds);
            Assert.True(item.AllowsModel(2));
            Assert.False(item.AllowsModel(4));
        }

        private static AppearanceItemInfo Item(ushort id, byte slotIndex) {
            return new AppearanceItemInfo(id, slotIndex, 1, null);
        }

        private static void AssertRefused(ushort defaultModelId, AppearanceModelInfo[] models, params AppearanceItemInfo[] items) {
            Assert.ThrowsAny<ArgumentException>(() => new AppearanceCatalogTable(defaultModelId, models, items));
        }
    }
}
