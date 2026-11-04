using System;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Paints atlas UVs onto <see cref="FacetMesh"/> faces: one UV per face, so each facet reads as one
    /// flat colour from a gradient atlas cell.
    /// </summary>
    /// <remarks>
    /// Band semantics match the Blender kit's <c>atlaslib.paint</c> band: t = 1 − (y − yMin)/(yMax − yMin),
    /// clamped to [0, 1], so the low end (t = 1) is the darkest end of an atlas gradient. Unlike Blender
    /// the t is per face (its centroid height), not per corner.
    /// </remarks>
    public static class FacetUv {
        /// <summary>Paints every face by its centroid height between <paramref name="yMin"/> and <paramref name="yMax"/>.</summary>
        public static void BandByHeight(FacetMesh mesh, Func<double, Vector2> uvForT, double yMin, double yMax) {
            if (mesh == null) {
                throw new ArgumentNullException(nameof(mesh));
            }
            BandByHeight(mesh, uvForT, yMin, yMax, 0, mesh.FaceCount);
        }

        /// <summary>
        /// Paints faces [<paramref name="firstFace"/>, <paramref name="firstFace"/> + <paramref name="faceCount"/>)
        /// by centroid height, leaving the rest alone (for meshes appended from several pieces).
        /// </summary>
        public static void BandByHeight(FacetMesh mesh, Func<double, Vector2> uvForT, double yMin, double yMax, int firstFace, int faceCount) {
            if (mesh == null) {
                throw new ArgumentNullException(nameof(mesh));
            }
            if (uvForT == null) {
                throw new ArgumentNullException(nameof(uvForT));
            }
            if (!(yMax > yMin)) {
                throw new ArgumentException("yMax must exceed yMin.", nameof(yMax));
            }
            if (firstFace < 0 || faceCount < 0 || firstFace + faceCount > mesh.FaceCount) {
                throw new ArgumentOutOfRangeException(nameof(faceCount), faceCount, "Face range is outside the mesh.");
            }
            for (int face = firstFace; face < firstFace + faceCount; face++) {
                mesh.SetFaceUv(face, uvForT(BandT(mesh.FaceCentroid(face).Y, yMin, yMax)));
            }
        }

        /// <summary>The band t for height <paramref name="y"/>: 1 at <paramref name="yMin"/>, 0 at <paramref name="yMax"/>, clamped.</summary>
        public static double BandT(double y, double yMin, double yMax) {
            double t = 1.0 - (y - yMin) / (yMax - yMin);
            if (t < 0.0) {
                return 0.0;
            }
            return t > 1.0 ? 1.0 : t;
        }

        /// <summary>Paints <paramref name="uv"/> on every face <paramref name="faceFilter"/> accepts (all when null); returns the count painted.</summary>
        public static int PaintFaces(FacetMesh mesh, Predicate<int> faceFilter, Vector2 uv) {
            if (mesh == null) {
                throw new ArgumentNullException(nameof(mesh));
            }
            int painted = 0;
            for (int face = 0; face < mesh.FaceCount; face++) {
                if (faceFilter != null && !faceFilter(face)) {
                    continue;
                }
                mesh.SetFaceUv(face, uv);
                painted++;
            }
            return painted;
        }
    }
}
