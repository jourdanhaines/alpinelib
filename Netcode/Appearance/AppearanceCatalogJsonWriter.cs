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
    /// the committed file untouched. A model's <c>requiredSlots</c> is written only when it has any.
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
            string required = model.RequiredSlotMask == 0 ? string.Empty : ", \"requiredSlots\": " + NumberList(model.RequiredSlots());
            return "{ \"id\": " + Number(model.Id)
                + ", \"slotCount\": " + Number(model.SlotCount)
                + ", \"pawnPrefabId\": " + Number(model.PawnPrefabId) + required + " }";
        }

        private static string WriteItem(AppearanceItemInfo item) {
            return "{ \"id\": " + Number(item.Id)
                + ", \"slotIndex\": " + Number(item.SlotIndex)
                + ", \"variantCount\": " + Number(item.VariantCount)
                + ", \"allowedModelIds\": " + NumberList(item.AllowedModelIds) + " }";
        }

        private static string NumberList(IReadOnlyList<byte> values) {
            var widened = new List<ushort>(values.Count);
            foreach (byte value in values) widened.Add(value);

            return NumberList(widened);
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
