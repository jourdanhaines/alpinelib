using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A flat-shaded triangle soup: every face owns its three corners, so each face can carry its own
    /// normal and atlas UV with no shared-vertex seams. The engine-free output of the faceted builders.
    /// </summary>
    /// <remarks>
    /// <see cref="Positions"/> and <see cref="Uvs"/> hold three entries per face, in step. Front faces wind
    /// so that <c>cross(b − a, c − a)</c> points outward, which is Unity's front-face convention, so
    /// positions copy straight into a <c>Mesh</c>. Not thread-safe; build one per thread.
    /// </remarks>
    public sealed class FacetMesh {
        /// <summary>An empty mesh.</summary>
        public FacetMesh() : this(0) {
        }

        /// <summary>An empty mesh with room for <paramref name="faceCapacity"/> faces.</summary>
        public FacetMesh(int faceCapacity) {
            if (faceCapacity < 0) {
                throw new ArgumentOutOfRangeException(nameof(faceCapacity), faceCapacity, "Must not be negative.");
            }
            Positions = new List<Vector3>(faceCapacity * 3);
            Uvs = new List<Vector2>(faceCapacity * 3);
        }

        /// <summary>Corner positions, three per face. Keep in step with <see cref="Uvs"/>.</summary>
        public List<Vector3> Positions { get; }

        /// <summary>Corner UVs, three per face. The faceted builders give all three corners one UV.</summary>
        public List<Vector2> Uvs { get; }

        /// <summary>Number of faces.</summary>
        public int FaceCount => Positions.Count / 3;

        /// <summary>The axis-aligned bounds of every corner. O(corners); cache it if read often.</summary>
        public FacetBounds Bounds {
            get {
                FacetBounds bounds = FacetBounds.Empty;
                for (int index = 0; index < Positions.Count; index++) {
                    bounds = bounds.Encapsulate(Positions[index]);
                }
                return bounds;
            }
        }

        /// <summary>Adds a face with UV (0, 0) and returns its index.</summary>
        public int AddFace(Vector3 a, Vector3 b, Vector3 c) {
            return AddFace(a, b, c, Vector2.Zero);
        }

        /// <summary>Adds a face whose three corners share <paramref name="uv"/> and returns its index.</summary>
        public int AddFace(Vector3 a, Vector3 b, Vector3 c, Vector2 uv) {
            int face = FaceCount;
            Positions.Add(a);
            Positions.Add(b);
            Positions.Add(c);
            Uvs.Add(uv);
            Uvs.Add(uv);
            Uvs.Add(uv);
            return face;
        }

        /// <summary>Sets all three corner UVs of <paramref name="face"/>.</summary>
        public void SetFaceUv(int face, Vector2 uv) {
            int corner = face * 3;
            Uvs[corner] = uv;
            Uvs[corner + 1] = uv;
            Uvs[corner + 2] = uv;
        }

        /// <summary>The mean of the face's three corners.</summary>
        public Vector3 FaceCentroid(int face) {
            int corner = face * 3;
            return (Positions[corner] + Positions[corner + 1] + Positions[corner + 2]) / 3f;
        }

        /// <summary>The face's unit outward normal, or zero for a degenerate (zero-area) face.</summary>
        public Vector3 FaceNormal(int face) {
            int corner = face * 3;
            Vector3 a = Positions[corner];
            Vector3 cross = Vector3.Cross(Positions[corner + 1] - a, Positions[corner + 2] - a);
            float length = cross.Length();
            if (length <= 0f || float.IsNaN(length)) {
                return Vector3.Zero;
            }
            return cross / length;
        }

        /// <summary>Appends every face of <paramref name="other"/> unchanged.</summary>
        public void Append(FacetMesh other) {
            Append(other, Matrix4x4.Identity);
        }

        /// <summary>
        /// Appends every face of <paramref name="other"/> (which may be this mesh) transformed by
        /// <paramref name="transform"/> (row-vector <c>System.Numerics</c> convention). A mirroring
        /// transform flips the winding back so faces stay front-facing.
        /// </summary>
        public void Append(FacetMesh other, Matrix4x4 transform) {
            if (other == null) {
                throw new ArgumentNullException(nameof(other));
            }
            int cornerCount = other.Positions.Count;
            bool mirrors = transform.GetDeterminant() < 0f;
            for (int corner = 0; corner < cornerCount; corner += 3) {
                AppendTransformedFace(other, corner, transform, mirrors);
            }
        }

        /// <summary>
        /// FNV-1a 64 over the exact float bits of every corner position, in order: a fingerprint for
        /// determinism checks (UVs are not included).
        /// </summary>
        public ulong ComputePositionHash() {
            ulong hash = StableHash.Fnv1a64OffsetBasis;
            for (int index = 0; index < Positions.Count; index++) {
                Vector3 position = Positions[index];
                hash = StableHash.Fnv1a64Append(hash, (uint)BitConverter.SingleToInt32Bits(position.X));
                hash = StableHash.Fnv1a64Append(hash, (uint)BitConverter.SingleToInt32Bits(position.Y));
                hash = StableHash.Fnv1a64Append(hash, (uint)BitConverter.SingleToInt32Bits(position.Z));
            }
            return hash;
        }

        /// <summary>Removes every face, keeping capacity.</summary>
        public void Clear() {
            Positions.Clear();
            Uvs.Clear();
        }

        private void AppendTransformedFace(FacetMesh other, int corner, Matrix4x4 transform, bool mirrors) {
            int second = mirrors ? corner + 2 : corner + 1;
            int third = mirrors ? corner + 1 : corner + 2;
            Positions.Add(Vector3.Transform(other.Positions[corner], transform));
            Positions.Add(Vector3.Transform(other.Positions[second], transform));
            Positions.Add(Vector3.Transform(other.Positions[third], transform));
            Uvs.Add(other.Uvs[corner]);
            Uvs.Add(other.Uvs[second]);
            Uvs.Add(other.Uvs[third]);
        }
    }
}
