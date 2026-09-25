using System;
using AlpineLib.Netcode.Appearance;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The catalog JSON the exporter commits: exact shape, and byte-for-byte stable however the source
    /// rows were ordered, so a re-export of an unchanged catalog leaves git clean.
    /// </summary>
    public sealed class AppearanceCatalogJsonTests {
        [Fact]
        public void TheDocumentHasTheExactExpectedShape() {
            var table = new AppearanceCatalogTable(
                1,
                new[] { new AppearanceModelInfo(1, 8, 0), new AppearanceModelInfo(2, 3, 4) },
                new[] {
                    new AppearanceItemInfo(1, 0, 2, new ushort[] { 1 }),
                    new AppearanceItemInfo(2, 1, 1, null),
                });

            string expected =
                "{\n" +
                "  \"defaultModelId\": 1,\n" +
                "  \"models\": [\n" +
                "    { \"id\": 1, \"slotCount\": 8, \"pawnPrefabId\": 0 },\n" +
                "    { \"id\": 2, \"slotCount\": 3, \"pawnPrefabId\": 4 }\n" +
                "  ],\n" +
                "  \"items\": [\n" +
                "    { \"id\": 1, \"slotIndex\": 0, \"variantCount\": 2, \"allowedModelIds\": [1] },\n" +
                "    { \"id\": 2, \"slotIndex\": 1, \"variantCount\": 1, \"allowedModelIds\": [] }\n" +
                "  ]\n" +
                "}\n";

            Assert.Equal(expected, AppearanceCatalogJsonWriter.Write(table));
        }

        [Fact]
        public void AnEmptyItemListIsWrittenInline() {
            var table = new AppearanceCatalogTable(1, new[] { new AppearanceModelInfo(1, 1, 0) }, Array.Empty<AppearanceItemInfo>());

            string json = AppearanceCatalogJsonWriter.Write(table);

            Assert.Contains("  \"items\": []\n}\n", json);
        }

        [Fact]
        public void OutputDoesNotDependOnSourceOrder() {
            var forward = new AppearanceCatalogTable(
                1,
                new[] { new AppearanceModelInfo(1, 4, 0), new AppearanceModelInfo(2, 4, 0) },
                new[] {
                    new AppearanceItemInfo(3, 0, 1, new ushort[] { 1, 2 }),
                    new AppearanceItemInfo(7, 2, 3, new ushort[] { 2 }),
                });
            var reversed = new AppearanceCatalogTable(
                1,
                new[] { new AppearanceModelInfo(2, 4, 0), new AppearanceModelInfo(1, 4, 0) },
                new[] {
                    new AppearanceItemInfo(7, 2, 3, new ushort[] { 2 }),
                    new AppearanceItemInfo(3, 0, 1, new ushort[] { 2, 1 }),
                });

            string first = AppearanceCatalogJsonWriter.Write(forward);

            Assert.Equal(first, AppearanceCatalogJsonWriter.Write(reversed));
            Assert.Equal(first, AppearanceCatalogJsonWriter.Write(forward));
            Assert.Contains("\"allowedModelIds\": [1, 2]", first);
            Assert.DoesNotContain("\r", first);
            Assert.EndsWith("}\n", first);
        }

        [Fact]
        public void ANullTableIsRefused() {
            Assert.Throws<ArgumentNullException>(() => AppearanceCatalogJsonWriter.Write(null));
        }
    }
}
