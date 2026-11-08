using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// One cell-unique mesh a worker builds for a <see cref="FarPropCell"/> (a landmark's far slice, say), drawn once
    /// at the cell centre through <see cref="FarPropCell.PayloadMaterials"/>[<see cref="MaterialSlot"/>]. Plain arrays
    /// only: the layer turns it into a <c>Mesh</c> on the main thread. Validated on construction, so a bad payload
    /// fails the worker's prepare (and is retried) instead of the upload.
    /// </summary>
    public sealed class FarPropMeshPayload {
        /// <summary>Most vertices a 16-bit index buffer addresses; more upload with 32-bit indices.</summary>
        public const int MaxUInt16Vertices = 65535;

        /// <summary>
        /// Triangles <paramref name="indices"/> over <paramref name="vertices"/> (cell-local metres) with
        /// <paramref name="uv0"/>, optional <paramref name="normals"/> (null: recalculated on upload) and
        /// <paramref name="colors"/> (null: none), drawn through material slot <paramref name="materialSlot"/>
        /// inside cell-local <paramref name="bounds"/>. Arrays are kept, not copied.
        /// </summary>
        public FarPropMeshPayload(Vector3[] vertices, Vector3[] normals, Vector2[] uv0, Color32[] colors, int[] indices, int materialSlot, Bounds bounds) {
            Validate(vertices, normals, uv0, colors, indices, materialSlot, bounds);
            Vertices = vertices;
            Normals = normals;
            Uv0 = uv0;
            Colors = colors;
            Indices = indices;
            MaterialSlot = materialSlot;
            Bounds = bounds;
        }

        public Vector3[] Vertices { get; }

        /// <summary>Per-vertex normals, or null to recalculate them on upload.</summary>
        public Vector3[] Normals { get; }

        public Vector2[] Uv0 { get; }

        /// <summary>Per-vertex colours, or null for none.</summary>
        public Color32[] Colors { get; }

        /// <summary>Triangle list into <see cref="Vertices"/>.</summary>
        public int[] Indices { get; }

        /// <summary>Index into the owning cell's <see cref="FarPropCell.PayloadMaterials"/>.</summary>
        public int MaterialSlot { get; }

        /// <summary>Cell-local bounds of the mesh.</summary>
        public Bounds Bounds { get; }

        /// <summary>True when the mesh needs a 32-bit index buffer.</summary>
        public bool NeedsUInt32Indices => Vertices.Length > MaxUInt16Vertices;

        /// <summary>Throws when the arrays cannot form one triangle mesh.</summary>
        public static void Validate(Vector3[] vertices, Vector3[] normals, Vector2[] uv0, Color32[] colors, int[] indices, int materialSlot, Bounds bounds) {
            if (vertices == null || vertices.Length == 0) throw new ArgumentException("Need at least one vertex.", nameof(vertices));
            if (uv0 == null || uv0.Length != vertices.Length) throw new ArgumentException("Need one uv0 per vertex.", nameof(uv0));
            if (normals != null && normals.Length != vertices.Length) throw new ArgumentException("Need one normal per vertex (or none).", nameof(normals));
            if (colors != null && colors.Length != vertices.Length) throw new ArgumentException("Need one colour per vertex (or none).", nameof(colors));
            if (indices == null || indices.Length == 0 || indices.Length % 3 != 0) throw new ArgumentException("Need a non-empty triangle list.", nameof(indices));
            if (materialSlot < 0) throw new ArgumentOutOfRangeException(nameof(materialSlot), materialSlot, "Must not be negative.");
            RequireBounds(bounds);

            int vertexCount = vertices.Length;
            foreach (int index in indices) {
                if (index < 0 || index >= vertexCount) throw new ArgumentOutOfRangeException(nameof(indices), index, $"Index outside the {vertexCount} vertices.");
            }
        }

        // A default (zero-size), inverted or non-finite box would cull the mesh wrongly; a flat box (one axis 0) is fine.
        private static void RequireBounds(Bounds bounds) {
            Vector3 size = bounds.size;
            bool isFinite = float.IsFinite(size.x) && float.IsFinite(size.y) && float.IsFinite(size.z) && float.IsFinite(bounds.center.x) && float.IsFinite(bounds.center.y) && float.IsFinite(bounds.center.z);
            if (!isFinite || size.x < 0f || size.y < 0f || size.z < 0f) throw new ArgumentException("Bounds must be finite and not inverted.", nameof(bounds));
            if (size.x + size.y + size.z <= 0f) throw new ArgumentException("Bounds must not be zero-size.", nameof(bounds));
        }

        /// <summary>
        /// Main thread: a new mesh of this payload (owned by the caller), 32-bit indexed when needed, its CPU copy
        /// released once uploaded (not readable afterwards).
        /// </summary>
        public Mesh CreateMesh(string name) {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.indexFormat = NeedsUInt32Indices ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Vertices);
            mesh.SetUVs(0, Uv0);
            if (Colors != null) mesh.SetColors(Colors);
            mesh.SetIndices(Indices, MeshTopology.Triangles, 0, false);
            if (Normals != null) mesh.SetNormals(Normals);
            else mesh.RecalculateNormals();
            mesh.bounds = Bounds;
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
