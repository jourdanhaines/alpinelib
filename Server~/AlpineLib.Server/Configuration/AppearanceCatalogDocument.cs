using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Appearance;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// JSON mirror of the exported appearance catalog: the character models and the items a member may
    /// wear, which is all the server needs to judge an outfit request.
    /// </summary>
    public sealed class AppearanceCatalogDocument {
        /// <summary>The model a member with no recognised choice is given.</summary>
        public ushort DefaultModelId { get; set; }

        public List<AppearanceModelDocument> Models { get; set; } = new List<AppearanceModelDocument>();

        public List<AppearanceItemDocument> Items { get; set; } = new List<AppearanceItemDocument>();

        /// <summary>Builds the validated table the server runs by.</summary>
        /// <param name="sourcePath">Where the document came from, for the error message only.</param>
        /// <exception cref="InvalidOperationException">A row breaks the catalog's rules.</exception>
        public AppearanceCatalogTable ToTable(string sourcePath = null) {
            try {
                return new AppearanceCatalogTable(DefaultModelId, BuildModels(), BuildItems());
            }
            catch (ArgumentException error) {
                throw new InvalidOperationException(
                    "Appearance catalog '" + (sourcePath ?? string.Empty) + "' is invalid: " + error.Message, error);
            }
        }

        private List<AppearanceModelInfo> BuildModels() {
            var models = new List<AppearanceModelInfo>();

            if (Models == null) {
                return models;
            }

            for (int modelIndex = 0; modelIndex < Models.Count; modelIndex++) {
                models.Add(RequireRow(Models[modelIndex], "models", modelIndex).ToInfo());
            }

            return models;
        }

        private List<AppearanceItemInfo> BuildItems() {
            var items = new List<AppearanceItemInfo>();

            if (Items == null) {
                return items;
            }

            for (int itemIndex = 0; itemIndex < Items.Count; itemIndex++) {
                items.Add(RequireRow(Items[itemIndex], "items", itemIndex).ToInfo());
            }

            return items;
        }

        private static TRow RequireRow<TRow>(TRow row, string listName, int rowIndex) where TRow : class {
            if (row == null) {
                throw new ArgumentException(listName + "[" + rowIndex.ToString() + "] is null.");
            }

            return row;
        }
    }
}
