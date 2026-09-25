using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// Writes an <see cref="AppearanceCatalogTable"/> as the JSON the dedicated server loads.
    /// </summary>
    /// <remarks>
    /// Output is byte-for-byte deterministic — rows ascending by id, fixed two-space indent, one row per
    /// line, <c>\n</c> line endings and a trailing newline — so a re-export of an unchanged catalog leaves
    /// the committed file untouched.
    /// </remarks>
    public static class AppearanceCatalogJsonWriter {
        private const string Indent = "  ";

        /// <summary>The JSON document for <paramref name="table"/>.</summary>
        public static string Write(AppearanceCatalogTable table) {
            if (table == null) {
                throw new ArgumentNullException(nameof(table));
            }

            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append(Indent).Append("\"defaultModelId\": ").Append(Number(table.DefaultModelId)).Append(",\n");
            AppendRows(builder, "models", table.Models, WriteModel);
            builder.Append(",\n");
            AppendRows(builder, "items", table.Items, WriteItem);
            builder.Append("\n}\n");
            return builder.ToString();
        }

        private static void AppendRows<TRow>(StringBuilder builder, string name, IReadOnlyList<TRow> rows, Func<TRow, string> writeRow) {
            builder.Append(Indent).Append('"').Append(name).Append("\": [");
            if (rows.Count == 0) {
                builder.Append(']');
                return;
            }

            builder.Append('\n');
            for (int index = 0; index < rows.Count; index++) {
                builder.Append(Indent).Append(Indent).Append(writeRow(rows[index]));
                builder.Append(index < rows.Count - 1 ? ",\n" : "\n");
            }

            builder.Append(Indent).Append(']');
        }

        private static string WriteModel(AppearanceModelInfo model) {
            return "{ \"id\": " + Number(model.Id)
                + ", \"slotCount\": " + Number(model.SlotCount)
                + ", \"pawnPrefabId\": " + Number(model.PawnPrefabId) + " }";
        }

        private static string WriteItem(AppearanceItemInfo item) {
            return "{ \"id\": " + Number(item.Id)
                + ", \"slotIndex\": " + Number(item.SlotIndex)
                + ", \"variantCount\": " + Number(item.VariantCount)
                + ", \"allowedModelIds\": " + NumberList(item.AllowedModelIds) + " }";
        }

        private static string NumberList(IReadOnlyList<ushort> values) {
            var builder = new StringBuilder("[");
            for (int index = 0; index < values.Count; index++) {
                if (index > 0) builder.Append(", ");
                builder.Append(Number(values[index]));
            }

            return builder.Append(']').ToString();
        }

        private static string Number(int value) {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
