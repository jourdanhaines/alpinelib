using System;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A box turned about +Y by whole degrees (Unity yaw: right (cos, 0, −sin), forward (sin, 0, cos)): centre
    /// and half-extents along its own right, up and forward axes. One piece of a <see cref="BoxEnvelopeFitter"/> result.
    /// </summary>
    public readonly struct EnvelopeBox {
        /// <summary>Builds a box.</summary>
        public EnvelopeBox(Vector3 center, Vector3 halfExtents, int yawDegrees) {
            Center = center;
            HalfExtents = halfExtents;
            YawDegrees = yawDegrees;
        }

        /// <summary>Centre in the mesh's space.</summary>
        public Vector3 Center { get; }

        /// <summary>Half-extents along the box's right, up and forward axes.</summary>
        public Vector3 HalfExtents { get; }

        /// <summary>Yaw about +Y, whole degrees in [0, 90).</summary>
        public int YawDegrees { get; }

        /// <summary>Volume, cubic units.</summary>
        public double Volume => 8.0 * HalfExtents.X * HalfExtents.Y * HalfExtents.Z;

        /// <summary>How far <paramref name="point"/> lies outside the box; 0 on or inside it.</summary>
        public double DistanceOutside(Vector3 point) {
            double radians = YawDegrees * Math.PI / 180.0;
            double sine = Math.Sin(radians);
            double cosine = Math.Cos(radians);
            double offsetX = point.X - Center.X;
            double offsetZ = point.Z - Center.Z;
            double localX = offsetX * cosine - offsetZ * sine;
            double localZ = offsetX * sine + offsetZ * cosine;
            double outsideX = Math.Max(0.0, Math.Abs(localX) - HalfExtents.X);
            double outsideY = Math.Max(0.0, Math.Abs(point.Y - Center.Y) - HalfExtents.Y);
            double outsideZ = Math.Max(0.0, Math.Abs(localZ) - HalfExtents.Z);
            return Math.Sqrt(outsideX * outsideX + outsideY * outsideY + outsideZ * outsideZ);
        }
    }
}
