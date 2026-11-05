using System;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>A growing box of <see cref="BoxEnvelopeFitter"/> in its own yaw frame (local x along right, z along forward).</summary>
    internal sealed class BoxEnvelopeCandidate {
        private const double DegreesToRadians = Math.PI / 180.0;

        public BoxEnvelopeCandidate(int yawDegrees) {
            YawDegrees = yawDegrees;
            Sine = Math.Sin(yawDegrees * DegreesToRadians);
            Cosine = Math.Cos(yawDegrees * DegreesToRadians);
            MinX = MinY = MinZ = double.PositiveInfinity;
            MaxX = MaxY = MaxZ = double.NegativeInfinity;
        }

        public int YawDegrees { get; }

        public double Sine { get; }

        public double Cosine { get; }

        public double MinX { get; private set; }

        public double MaxX { get; private set; }

        public double MinY { get; private set; }

        public double MaxY { get; private set; }

        public double MinZ { get; private set; }

        public double MaxZ { get; private set; }

        public bool IsEmpty => MinX > MaxX;

        public double Volume => IsEmpty ? 0.0 : (MaxX - MinX) * (MaxY - MinY) * (MaxZ - MinZ);

        /// <summary>Volume below height <paramref name="height"/>.</summary>
        public double VolumeBelow(double height) {
            return IsEmpty ? 0.0 : (MaxX - MinX) * Math.Max(0.0, Math.Min(MaxY, height) - MinY) * (MaxZ - MinZ);
        }

        public void Include(double x, double y, double z) {
            double localX = x * Cosine - z * Sine;
            double localZ = x * Sine + z * Cosine;
            MinX = Math.Min(MinX, localX);
            MaxX = Math.Max(MaxX, localX);
            MinY = Math.Min(MinY, y);
            MaxY = Math.Max(MaxY, y);
            MinZ = Math.Min(MinZ, localZ);
            MaxZ = Math.Max(MaxZ, localZ);
        }

        /// <summary>Grows this box to hold every corner of <paramref name="other"/>.</summary>
        public void IncludeBox(BoxEnvelopeCandidate other) {
            for (int corner = 0; corner < 4; corner++) {
                other.PlanCorner(corner, out double x, out double z);
                Include(x, other.MinY, z);
                Include(x, other.MaxY, z);
            }
        }

        /// <summary>True when <paramref name="point"/> lies deeper than <paramref name="tolerance"/> inside on every axis.</summary>
        public bool Holds(Vector3 point, double tolerance) {
            double localX = point.X * Cosine - point.Z * Sine;
            double localZ = point.X * Sine + point.Z * Cosine;
            return localX > MinX + tolerance && localX < MaxX - tolerance && point.Y > MinY + tolerance && point.Y < MaxY - tolerance
                && localZ > MinZ + tolerance && localZ < MaxZ - tolerance;
        }

        /// <summary>Volume shared with <paramref name="other"/>, exact for a shared yaw and 0 otherwise.</summary>
        public double Overlap(BoxEnvelopeCandidate other) {
            return OverlapBelow(other, double.PositiveInfinity);
        }

        /// <summary>As <see cref="Overlap"/>, counting only what lies below height <paramref name="height"/>.</summary>
        public double OverlapBelow(BoxEnvelopeCandidate other, double height) {
            if (other.YawDegrees != YawDegrees) return 0.0;

            double x = Math.Min(MaxX, other.MaxX) - Math.Max(MinX, other.MinX);
            double y = Math.Min(Math.Min(MaxY, other.MaxY), height) - Math.Max(MinY, other.MinY);
            double z = Math.Min(MaxZ, other.MaxZ) - Math.Max(MinZ, other.MinZ);
            return x > 0.0 && y > 0.0 && z > 0.0 ? x * y * z : 0.0;
        }

        /// <summary>Plan corner <paramref name="corner"/> (0–3) in mesh space.</summary>
        public void PlanCorner(int corner, out double x, out double z) {
            double localX = corner == 0 || corner == 3 ? MinX : MaxX;
            double localZ = corner < 2 ? MinZ : MaxZ;
            x = localX * Cosine + localZ * Sine;
            z = -localX * Sine + localZ * Cosine;
        }

        /// <summary>The box with its centre on the <paramref name="snap"/> grid and half-extents rounded outward to it.</summary>
        public EnvelopeBox ToBox(double snap) {
            double localX = (MinX + MaxX) * 0.5;
            double localZ = (MinZ + MaxZ) * 0.5;
            double centreX = Round(localX * Cosine + localZ * Sine, snap);
            double centreY = Round((MinY + MaxY) * 0.5, snap);
            double centreZ = Round(-localX * Sine + localZ * Cosine, snap);
            // Rounding the centre moves it at most half a step along each mesh axis, so up to that much along a turned axis too.
            double slack = snap * 0.75;
            var half = new Vector3((float)Ceiling((MaxX - MinX) * 0.5 + slack, snap), (float)Ceiling((MaxY - MinY) * 0.5 + slack, snap),
                (float)Ceiling((MaxZ - MinZ) * 0.5 + slack, snap));
            return new EnvelopeBox(new Vector3((float)centreX, (float)centreY, (float)centreZ), half, YawDegrees);
        }

        public BoxEnvelopeCandidate Copy() {
            var copy = new BoxEnvelopeCandidate(YawDegrees);
            copy.IncludeBox(this);
            return copy;
        }

        private static double Round(double value, double snap) {
            return Math.Round(value / snap, MidpointRounding.AwayFromZero) * snap;
        }

        private static double Ceiling(double value, double snap) {
            return Math.Ceiling(value / snap - 1e-9) * snap;
        }
    }
}
