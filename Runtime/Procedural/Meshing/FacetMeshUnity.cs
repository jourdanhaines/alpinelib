using System;
using AlpineLib.Procedural.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Procedural.Kits {
    /// <summary>
    /// Uploads an engine-free <see cref="FacetMesh"/> into a Unity <see cref="Mesh"/>: flat per-face
    /// normals, explicit bounds, one UV channel, never saved with scenes.
    /// </summary>
    /// <remarks>
    /// Main thread only (Unity API). Coordinates and winding copy straight across. Degenerate faces get an
    /// up normal. The caller owns the mesh and must <c>Object.Destroy</c> it.
    /// </remarks>
    public static class FacetMeshUnity {
        private const int MaxUInt16Vertices = 65535;

        /// <summary>A new <c>DontSave</c> mesh named <paramref name="name"/> holding <paramref name="facets"/>.</summary>
        public static Mesh ToMesh(FacetMesh facets, string name) {
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            Write(facets, mesh);
            return mesh;
        }

        /// <summary>Replaces the contents of <paramref name="mesh"/> with <paramref name="facets"/>.</summary>
        public static void Write(FacetMesh facets, Mesh mesh) {
            if (facets == null) {
                throw new ArgumentNullException(nameof(facets));
            }
            if (mesh == null) {
                throw new ArgumentNullException(nameof(mesh));
            }
            int cornerCount = facets.Positions.Count;
            if (cornerCount % 3 != 0 || facets.Uvs.Count != cornerCount) {
                throw new ArgumentException("FacetMesh needs three positions and three UVs per face.", nameof(facets));
            }
            Vector3[] vertices = new Vector3[cornerCount];
            Vector3[] normals = new Vector3[cornerCount];
            Vector2[] uvs = new Vector2[cornerCount];
            int[] triangles = new int[cornerCount];
            for (int face = 0; face < facets.FaceCount; face++) {
                FillFace(facets, face, vertices, normals, uvs, triangles);
            }
            mesh.Clear();
            mesh.indexFormat = cornerCount > MaxUInt16Vertices ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);
            mesh.bounds = ToBounds(facets.Bounds);
        }

        private static void FillFace(FacetMesh facets, int face, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles) {
            System.Numerics.Vector3 faceNormal = facets.FaceNormal(face);
            Vector3 normal = faceNormal == System.Numerics.Vector3.Zero ? Vector3.up : ToUnity(faceNormal);
            int first = face * 3;
            for (int corner = first; corner < first + 3; corner++) {
                vertices[corner] = ToUnity(facets.Positions[corner]);
                normals[corner] = normal;
                System.Numerics.Vector2 uv = facets.Uvs[corner];
                uvs[corner] = new Vector2(uv.X, uv.Y);
                triangles[corner] = corner;
            }
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 value) {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static Bounds ToBounds(FacetBounds bounds) {
            if (bounds.IsEmpty) {
                return new Bounds(Vector3.zero, Vector3.zero);
            }
            return new Bounds(ToUnity(bounds.Centre), ToUnity(bounds.Size));
        }
    }
}
