using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// The ground annulus as a flat square lattice anchored in absolute xz and vertex-coloured (sRGB) from the
    /// consumer's ground, so the annulus carries on the streamed world's own colours past its edge. Triangles
    /// wholly inside the hole or beyond the outer radius are left out. Positions are relative to the anchor; only the
    /// colours change when the anchor moves, and a move by whole lattice steps shifts the known colours across and
    /// samples only the strips that came into the lattice. Main thread.
    /// </summary>
    public sealed class BackdropGroundLattice : IDisposable {
        private readonly Func<double, double, Color32> _colourAt;
        private readonly Vector3[] _positions;
        private Color32[] _colours;
        private Color32[] _previous;
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
            _previous = new Color32[_positions.Length];
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

        /// <summary>Vertices the last <see cref="Recolour"/> sampled from the colour source (the rest were shifted across).</summary>
        public int LastSampled { get; private set; }

        /// <summary>The vertex colours (sRGB) as last uploaded; read only.</summary>
        public Color32[] Colours => _colours;

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

            if (!IsColoured || !TryShift(anchorX, anchorZ)) SampleAll(anchorX, anchorZ);
            Apply(anchorX, anchorZ);
        }

        /// <summary>Recolours about absolute (<paramref name="anchorX"/>, <paramref name="anchorZ"/>) sampling every vertex, as a jump or the first colouring does.</summary>
        public void RecolourAll(double anchorX, double anchorZ) {
            SampleAll(anchorX, anchorZ);
            Apply(anchorX, anchorZ);
        }

        public void Dispose() {
            if (Mesh == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(Mesh);
            else UnityEngine.Object.DestroyImmediate(Mesh);
            Mesh = null;
        }

        private void SampleAll(double anchorX, double anchorZ) {
            for (int index = 0; index < _positions.Length; index++) _colours[index] = Sample(index, anchorX, anchorZ);
            LastSampled = _positions.Length;
        }

        private void Apply(double anchorX, double anchorZ) {
            Mesh.colors32 = _colours;
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            IsColoured = true;
        }

        // A move by whole lattice steps (less than the lattice across): each vertex takes the colour of the one that stood
        // on its absolute point before (the same source input, bit for bit), and only vertices with none are sampled.
        private bool TryShift(double anchorX, double anchorZ) {
            double stepsX = (anchorX - AnchorX) / _spacing;
            double stepsZ = (anchorZ - AnchorZ) / _spacing;
            if (stepsX != Math.Round(stepsX) || stepsZ != Math.Round(stepsZ)) return false;
            if (Math.Abs(stepsX) >= _side || Math.Abs(stepsZ) >= _side) return false;

            (_colours, _previous) = (_previous, _colours);
            LastSampled = 0;
            for (int row = 0; row < _side; row++) ShiftRow(row, (int)stepsX, (int)stepsZ, anchorX, anchorZ);
            return true;
        }

        private void ShiftRow(int row, int shiftX, int shiftZ, double anchorX, double anchorZ) {
            int sourceRow = row + shiftZ;
            bool isRowKnown = sourceRow >= 0 && sourceRow < _side;
            for (int column = 0; column < _side; column++) {
                int sourceColumn = column + shiftX;
                int index = row * _side + column;
                if (isRowKnown && sourceColumn >= 0 && sourceColumn < _side) _colours[index] = _previous[sourceRow * _side + sourceColumn];
                else _colours[index] = SampleCounted(index, anchorX, anchorZ);
            }
        }

        private Color32 SampleCounted(int index, double anchorX, double anchorZ) {
            LastSampled++;
            return Sample(index, anchorX, anchorZ);
        }

        private Color32 Sample(int index, double anchorX, double anchorZ) {
            Vector3 local = _positions[index];
            return _colourAt(anchorX + local.x, anchorZ + local.z);
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
