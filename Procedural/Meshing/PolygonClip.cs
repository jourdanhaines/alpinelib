using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>Sutherland–Hodgman clipping of a convex polygon against an axis plane.</summary>
    internal static class PolygonClip {
        /// <summary>Writes into <paramref name="output"/> the part of <paramref name="input"/> on one side of coordinate <paramref name="axis"/> = level.</summary>
        public static void Clip(List<Vector3> input, List<Vector3> output, int axis, double level, bool keepAbove) {
            output.Clear();
            for (int index = 0; index < input.Count; index++) {
                Vector3 current = input[index];
                Vector3 next = input[(index + 1) % input.Count];
                double currentValue = Get(current, axis);
                double nextValue = Get(next, axis);
                bool currentIn = keepAbove ? currentValue >= level : currentValue <= level;
                bool nextIn = keepAbove ? nextValue >= level : nextValue <= level;
                if (currentIn) output.Add(current);
                if (currentIn == nextIn) continue;

                double share = (level - currentValue) / (nextValue - currentValue);
                output.Add(Set(current + (next - current) * (float)share, axis, (float)level));
            }
        }

        private static double Get(Vector3 point, int axis) {
            return axis == 0 ? point.X : axis == 1 ? point.Y : point.Z;
        }

        private static Vector3 Set(Vector3 point, int axis, float value) {
            if (axis == 0) point.X = value;
            else if (axis == 1) point.Y = value;
            else point.Z = value;
            return point;
        }
    }
}
