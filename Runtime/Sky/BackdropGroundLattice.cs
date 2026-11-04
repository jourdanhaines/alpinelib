using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// The ground annulus as a flat square lattice anchored in absolute xz and vertex-coloured (sRGB) from the
    /// consumer's ground, so the annulus carries on the streamed world's own colours past its edge. Triangles
    /// wholly inside the hole or beyond the outer radius are left out. Positions are relative to the anchor; only the
    /// colours change when the anchor moves. Main thread.
    /// </summary>
    public sealed class BackdropGroundLattice : IDisposable {
        private readonly Func<double, double, Color32> _colourAt;
        private readonly Vector3[] _positions;
        private readonly Color32[] _colours;
        private readonly int _side;
        private readonly double _spacing;
        private readonly double _snap;

        /// <summary>
        /// A lattice <paramref name="spacing"/> m apart reaching <paramref name="outerRadius"/> about its anchor, holed
        /// to <paramref name="holeRadius"/>, anchored on a <paramref name="snap"/> m grid, coloured by
        /// <paramref name="colourAt"/>(absolute x, absolute z).
        /// </summary>
        public BackdropGroundLattice(double spacing, double snap, double holeRadius, double outerRadius, Func<double, double, Color32> colourAt) {
            if (!(spacing > 0.0) || !(snap > 0.0)) throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing and snap must be positive.");
            if (!(outerRadius > holeRadius)) throw new ArgumentOutOfRangeException(nameof(outerRadius), outerRadius, "Must exceed the hole radius.");

            _colourAt = colourAt ?? throw new ArgumentNullException(nameof(colourAt));
            _spacing = spacing;
            _snap = snap;
            int cells = (int)Math.Ceiling(outerRadius / spacing) * 2;
            _side = cells + 1;
            _positions = new Vector3[_side * _side];
            _colours = new Color32[_positions.Length];
            double half = cells * 0.5 * spacing;
            for (int row = 0; row < _side; row++) {
                for (int column = 0; column < _side; column++) {
                    _positions[row * _side + column] = new Vector3((float)(column * spacing - half), 0f, (float)(row * spacing - half));
                }
            }

            Mesh = new Mesh { name = "Backdrop Ground Lattice", hideFlags = HideFlags.DontSave };
            Mesh.indexFormat = _positions.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            Mesh.vertices = _positions;
            Mesh.colors32 = _colours;
            Mesh.SetTriangles(Triangles(holeRadius, outerRadius), 0);
            var normals = new Vector3[_positions.Length];
            for (int index = 0; index < normals.Length; index++) normals[index] = Vector3.up;
            Mesh.normals = normals;
            Mesh.bounds = new Bounds(Vector3.zero, new Vector3((float)(half * 2.0), 1f, (float)(half * 2.0)));
        }

        /// <summary>The lattice mesh (owned; freed by <see cref="Dispose"/>).</summary>
        public Mesh Mesh { get; private set; }

        /// <summary>True once <see cref="Recolour"/> ran.</summary>
        public bool IsColoured { get; private set; }

        /// <summary>Absolute xz of the lattice's centre, from the last <see cref="Recolour"/>.</summary>
        public double AnchorX { get; private set; }
        public double AnchorZ { get; private set; }

        /// <summary>The anchor for a camera at absolute (<paramref name="x"/>, <paramref name="z"/>): the nearest snap grid point.</summary>
        public void AnchorFor(double x, double z, out double anchorX, out double anchorZ) {
            anchorX = Math.Round(x / _snap) * _snap;
            anchorZ = Math.Round(z / _snap) * _snap;
        }

        /// <summary>Recolours the lattice about absolute (<paramref name="anchorX"/>, <paramref name="anchorZ"/>) unless it is already there.</summary>
        public void Recolour(double anchorX, double anchorZ) {
            if (IsColoured && anchorX == AnchorX && anchorZ == AnchorZ) return;

            for (int index = 0; index < _positions.Length; index++) {
                Vector3 local = _positions[index];
                _colours[index] = _colourAt(anchorX + local.x, anchorZ + local.z);
            }
            Mesh.colors32 = _colours;
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            IsColoured = true;
        }

        public void Dispose() {
            if (Mesh == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(Mesh);
            else UnityEngine.Object.DestroyImmediate(Mesh);
            Mesh = null;
        }

        // Two triangles per cell (wound to face up), except those wholly in the hole or wholly beyond the outer radius.
        private int[] Triangles(double holeRadius, double outerRadius) {
            var triangles = new List<int>();
            double hole = holeRadius * holeRadius;
            double outer = outerRadius * outerRadius;
            for (int row = 0; row < _side - 1; row++) {
                for (int column = 0; column < _side - 1; column++) {
                    int corner = row * _side + column;
                    int north = corner + _side;
                    AddTriangle(triangles, hole, outer, corner, north, corner + 1);
                    AddTriangle(triangles, hole, outer, corner + 1, north, north + 1);
                }
            }
            return triangles.ToArray();
        }

        private void AddTriangle(List<int> triangles, double hole, double outer, int first, int second, int third) {
            if (!IsOutsideHole(hole, first) && !IsOutsideHole(hole, second) && !IsOutsideHole(hole, third)) return;
            if (!IsInsideOuter(outer, first) && !IsInsideOuter(outer, second) && !IsInsideOuter(outer, third)) return;

            triangles.Add(first);
            triangles.Add(second);
            triangles.Add(third);
        }

        private bool IsOutsideHole(double hole, int vertex) {
            return SquaredRadius(vertex) >= hole;
        }

        private bool IsInsideOuter(double outer, int vertex) {
            return SquaredRadius(vertex) <= outer;
        }

        private double SquaredRadius(int vertex) {
            Vector3 position = _positions[vertex];
            return position.x * (double)position.x + position.z * (double)position.z;
        }
    }
}
