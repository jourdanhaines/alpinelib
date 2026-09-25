using System;
using System.IO;
using AlpineLib.Netcode.Appearance;
using AlpineLib.Server.Configuration;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The catalog JSON the exporter commits: exact shape, byte-for-byte stable however the source rows
    /// were ordered, and read back by the server's loader into the same table.
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

        [Fact]
        public void TheLoaderReadsBackWhatTheWriterWrote() {
            AppearanceCatalogTable written = AppearanceTestCatalog.Build();

            AppearanceCatalogTable read = AppearanceCatalogLoader.Parse(AppearanceCatalogJsonWriter.Write(written), "roundtrip");

            AssertSameTable(written, read);
            Assert.Equal(AppearanceCatalogJsonWriter.Write(written), AppearanceCatalogJsonWriter.Write(read));
        }

        [Fact]
        public void TheLoaderFindsTheCatalogBesideTheSessionConfig() {
            string directory = Path.Combine(Path.GetTempPath(), "alpine-appearance-" + Guid.NewGuid().ToString("N"));
            string sessionConfigPath = Path.Combine(directory, "session-config.json");

            try {
                string catalogPath = AppearanceCatalogLoader.ResolvePath(sessionConfigPath);
                Directory.CreateDirectory(Path.GetDirectoryName(catalogPath));
                File.WriteAllText(catalogPath, AppearanceCatalogJsonWriter.Write(AppearanceTestCatalog.Build()));

                Assert.Equal(Path.Combine(directory, AppearanceCatalogLoader.FolderName, AppearanceCatalogLoader.FileName), catalogPath);
                AssertSameTable(AppearanceTestCatalog.Build(), AppearanceCatalogLoader.LoadFromFile(catalogPath));
            }
            finally {
                DeleteQuietly(directory);
            }
        }

        [Fact]
        public void AMissingCatalogFileStopsTheServer() {
            string missing = Path.Combine(Path.GetTempPath(), "alpine-appearance-" + Guid.NewGuid().ToString("N"), "appearance-catalog.json");

            Assert.Throws<InvalidOperationException>(() => AppearanceCatalogLoader.LoadFromFile(missing));
            Assert.Throws<InvalidOperationException>(() => AppearanceCatalogLoader.LoadFromFile(" "));
            Assert.Throws<InvalidOperationException>(() => AppearanceCatalogLoader.ResolvePath(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{ \"models\": [ ")]
        [InlineData("null")]
        [InlineData("{ \"defaultModelId\": \"one\" }")]
        [InlineData("{ \"defaultModelId\": 70000 }")]
        public void MalformedJsonIsRefused(string json) {
            Assert.Throws<InvalidOperationException>(() => AppearanceCatalogLoader.Parse(json, "bad"));
        }

        [Theory]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [], \"items\": [] }")]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 0 } ] }")]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 2 }, { \"id\": 1, \"slotCount\": 2 } ] }")]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 2 } ], \"items\": [ { \"id\": 3, \"slotIndex\": 0, \"variantCount\": 0 } ] }")]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 2 } ], \"items\": [ { \"id\": 3, \"slotIndex\": 0, \"variantCount\": 1, \"allowedModelIds\": [9] } ] }")]
        [InlineData("{ \"defaultModelId\": 1, \"models\": [ { \"id\": 1, \"slotCount\": 2 } ], \"items\": [ null ] }")]
        public void RowsThatBreakTheCatalogRulesAreRefused(string json) {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => AppearanceCatalogLoader.Parse(json, "rows"));

            Assert.Contains("rows", error.Message);
        }

        [Fact]
        public void TheLoaderIsForgivingAboutCaseCommentsAndTrailingCommas() {
            string json = "{ // exported by hand\n \"DefaultModelId\": 1, \"MODELS\": [ { \"id\": 1, \"slotCount\": 2, }, ], }";

            AppearanceCatalogTable table = AppearanceCatalogLoader.Parse(json, "lenient");

            Assert.Equal(1, table.DefaultModelId);
            Assert.Single(table.Models);
            Assert.Empty(table.Items);
        }

        private static void AssertSameTable(AppearanceCatalogTable expected, AppearanceCatalogTable actual) {
            Assert.Equal(expected.DefaultModelId, actual.DefaultModelId);
            Assert.Equal(expected.Models.Count, actual.Models.Count);
            Assert.Equal(expected.Items.Count, actual.Items.Count);

            for (int modelIndex = 0; modelIndex < expected.Models.Count; modelIndex++) {
                Assert.Equal(expected.Models[modelIndex].Id, actual.Models[modelIndex].Id);
                Assert.Equal(expected.Models[modelIndex].SlotCount, actual.Models[modelIndex].SlotCount);
                Assert.Equal(expected.Models[modelIndex].PawnPrefabId, actual.Models[modelIndex].PawnPrefabId);
            }

            for (int itemIndex = 0; itemIndex < expected.Items.Count; itemIndex++) {
                AssertSameItem(expected.Items[itemIndex], actual.Items[itemIndex]);
            }
        }

        private static void AssertSameItem(AppearanceItemInfo expected, AppearanceItemInfo actual) {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.SlotIndex, actual.SlotIndex);
            Assert.Equal(expected.VariantCount, actual.VariantCount);
            Assert.Equal(expected.AllowedModelIds, actual.AllowedModelIds);
        }

        private static void DeleteQuietly(string directory) {
            try {
                Directory.Delete(directory, true);
            }
            catch (IOException) {
                // A leftover temp directory is noise, not a failure.
            }
        }
    }
}
