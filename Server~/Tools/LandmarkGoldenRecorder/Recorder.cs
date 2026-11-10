using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.GoldenRecorder {
    /// <summary>Pass and fail counts of every compared call, the sampled case texts, and their literal formatting.</summary>
    public static class Recorder {
        public static readonly object Gate = new object();
        public static readonly SortedDictionary<string, (int Ok, int Bad)> Counts = new SortedDictionary<string, (int Ok, int Bad)>();
        public static readonly List<string> FilletCases = new List<string>();
        public static readonly List<string> SeatCases = new List<string>();

        public static bool Same(double first, double second) => BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);

        public static bool Same(PlanPoint first, PlanPoint second) => Same(first.X, second.X) && Same(first.Z, second.Z);

        public static bool Same(IReadOnlyList<PlanPoint> first, IReadOnlyList<PlanPoint> second) {
            if (first.Count != second.Count) return false;

            for (int index = 0; index < first.Count; index++) {
                if (!Same(first[index], second[index])) return false;
            }
            return true;
        }

        public static void Check(string name, bool isSame) {
            lock (Gate) {
                Counts.TryGetValue(name, out (int Ok, int Bad) counts);
                Counts[name] = isSame ? (counts.Ok + 1, counts.Bad) : (counts.Ok, counts.Bad + 1);
            }
        }

        /// <summary>A round-trip C# double literal.</summary>
        public static string Literal(double value) {
            if (BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(-0.0)) return "-0.0";

            string text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.IndexOf('.') < 0 && text.IndexOf('E') < 0 ? text + ".0" : text;
        }

        public static string Points(IReadOnlyList<PlanPoint> points) {
            var text = new StringBuilder("P(");
            for (int index = 0; index < points.Count; index++) {
                if (index > 0) text.Append(", ");
                text.Append(Literal(points[index].X)).Append(", ").Append(Literal(points[index].Z));
            }
            return text.Append(')').ToString();
        }

        public static string Polygons(IReadOnlyList<IReadOnlyList<PlanPoint>> polygons) {
            var text = new StringBuilder("Polygons(");
            for (int index = 0; index < polygons.Count; index++) {
                if (index > 0) text.Append(", ");
                text.Append(Points(polygons[index]));
            }
            return text.Append(')').ToString();
        }

        public static string Hex(ulong value) => "0x" + value.ToString("X16") + "UL";
    }
}
