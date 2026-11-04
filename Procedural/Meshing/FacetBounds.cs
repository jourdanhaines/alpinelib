using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>An axis-aligned box around a <see cref="FacetMesh"/>; empty when the mesh has no faces.</summary>
    public readonly struct FacetBounds {
        /// <summary>A box from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public FacetBounds(Vector3 min, Vector3 max) {
            Min = min;
            Max = max;
            IsEmpty = false;
        }

        private FacetBounds(bool isEmpty) {
            Min = Vector3.Zero;
            Max = Vector3.Zero;
            IsEmpty = isEmpty;
        }

        /// <summary>The bounds of nothing: zero-sized at the origin, <see cref="IsEmpty"/> set.</summary>
        public static FacetBounds Empty => new FacetBounds(true);

        /// <summary>The lowest corner.</summary>
        public Vector3 Min { get; }

        /// <summary>The highest corner.</summary>
        public Vector3 Max { get; }

        /// <summary>True when the box encloses no geometry.</summary>
        public bool IsEmpty { get; }

        /// <summary>The box centre.</summary>
        public Vector3 Centre => (Min + Max) * 0.5f;

        /// <summary>The box size along each axis.</summary>
        public Vector3 Size => Max - Min;

        /// <summary>This box grown to contain <paramref name="point"/>.</summary>
        public FacetBounds Encapsulate(Vector3 point) {
            if (IsEmpty) {
                return new FacetBounds(point, point);
            }
            return new FacetBounds(Vector3.Min(Min, point), Vector3.Max(Max, point));
        }
    }
}
