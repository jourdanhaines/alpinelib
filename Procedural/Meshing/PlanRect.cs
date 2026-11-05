using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>A rectangle of whole cells of a <see cref="PlanFrame"/> and the base cells it is responsible for.</summary>
    internal sealed class PlanRect {
        public PlanRect(PlanFrame frame, int minColumn, int maxColumn, int minRow, int maxRow) {
            Frame = frame;
            MinColumn = minColumn;
            MaxColumn = maxColumn;
            MinRow = minRow;
            MaxRow = maxRow;
        }

        public PlanFrame Frame { get; }
        public int MinColumn { get; }
        public int MaxColumn { get; }
        public int MinRow { get; }
        public int MaxRow { get; }
        public List<int> Cells { get; } = new List<int>();

        public long Area => (long)(MaxColumn - MinColumn + 1) * (MaxRow - MinRow + 1);

        public double MinLocalX => Frame.OriginX + MinColumn * Frame.Cell;
        public double MaxLocalX => Frame.OriginX + (MaxColumn + 1) * Frame.Cell;
        public double MinLocalZ => Frame.OriginZ + MinRow * Frame.Cell;
        public double MaxLocalZ => Frame.OriginZ + (MaxRow + 1) * Frame.Cell;

        /// <summary>World plan corner 0–3.</summary>
        public void Corner(int corner, out double x, out double z) {
            double localX = corner == 0 || corner == 3 ? MinLocalX : MaxLocalX;
            double localZ = corner < 2 ? MinLocalZ : MaxLocalZ;
            Frame.ToWorld(localX, localZ, out x, out z);
        }

        /// <summary>True when world point (x, z) lies inside, up to <paramref name="tolerance"/>.</summary>
        public bool Holds(double x, double z, double tolerance) {
            Frame.ToLocal(x, z, out double localX, out double localZ);
            return localX >= MinLocalX - tolerance && localX <= MaxLocalX + tolerance && localZ >= MinLocalZ - tolerance && localZ <= MaxLocalZ + tolerance;
        }
    }
}
