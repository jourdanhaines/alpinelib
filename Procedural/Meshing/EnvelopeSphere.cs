using System;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>A sphere around a look: one piece of a <see cref="SphereEnvelopeFitter"/> result.</summary>
    public readonly struct EnvelopeSphere {
        /// <summary>Builds a sphere.</summary>
        public EnvelopeSphere(Vector3 center, float radius) {
            Center = center;
            Radius = radius;
        }

        /// <summary>Centre in the mesh's space.</summary>
        public Vector3 Center { get; }

        /// <summary>Radius.</summary>
        public float Radius { get; }

        /// <summary>How far <paramref name="point"/> lies outside the sphere; 0 on or inside it.</summary>
        public double DistanceOutside(Vector3 point) {
            return Math.Max(0.0, Vector3.Distance(point, Center) - (double)Radius);
        }
    }
}
