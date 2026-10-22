using System;
using AlpineLib.Procedural.Grid;
using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>
    /// What a map viewport shows: a centre in absolute metres, a zoom (<see cref="MetresAcross"/> the
    /// viewport's width) and a rotation (viewport up points at bearing <see cref="RotationDegrees"/>,
    /// 0 = north-up). Viewport points are canvas units relative to the viewport centre, +y up. Plain
    /// class in double precision so pan/zoom maths is exact far from the origin and testable without a scene.
    /// </summary>
    public class MapView {
        /// <summary>Default closest zoom, metres across.</summary>
        public const double DefaultMinMetresAcross = 64.0;

        /// <summary>Default widest zoom, metres across.</summary>
        public const double DefaultMaxMetresAcross = 2048.0;

        private MapPoint _centre;
        private double _metresAcross;
        private double _rotationDegrees;
        private Vector2 _viewportSize = new Vector2(1f, 1f);
        private MapRect? _panBounds;

        /// <summary>A north-up view at the origin, <paramref name="metresAcross"/> wide, zoom limited to the given range.</summary>
        public MapView(double metresAcross = 362.0, double minMetresAcross = DefaultMinMetresAcross, double maxMetresAcross = DefaultMaxMetresAcross) {
            if (minMetresAcross <= 0.0 || maxMetresAcross < minMetresAcross) {
                throw new ArgumentOutOfRangeException(nameof(minMetresAcross), "Zoom limits must be positive and ordered.");
            }

            MinMetresAcross = minMetresAcross;
            MaxMetresAcross = maxMetresAcross;
            _metresAcross = ClampZoom(metresAcross);
        }

        /// <summary>Raised after any change to centre, zoom, rotation, size or bounds.</summary>
        public event Action Changed;

        /// <summary>Closest zoom.</summary>
        public double MinMetresAcross { get; }

        /// <summary>Widest zoom.</summary>
        public double MaxMetresAcross { get; }

        /// <summary>Map point at the viewport centre.</summary>
        public MapPoint Centre => _centre;

        /// <summary>Metres spanned by the viewport's width.</summary>
        public double MetresAcross => _metresAcross;

        /// <summary>Viewport size in canvas units.</summary>
        public Vector2 ViewportSize => _viewportSize;

        /// <summary>Metres per canvas unit.</summary>
        public double MetresPerUnit => _metresAcross / Math.Max(1e-6, _viewportSize.x);

        /// <summary>Bearing (degrees clockwise from north) the viewport's up points at.</summary>
        public double RotationDegrees => _rotationDegrees;

        /// <summary>Rect the centre is kept inside, or null for no limit.</summary>
        public MapRect? PanBounds => _panBounds;

        /// <summary>Sets the viewport size (canvas units); zero or negative sizes are ignored.</summary>
        public void SetViewportSize(Vector2 size) {
            if (size.x <= 0f || size.y <= 0f || size == _viewportSize) return;

            _viewportSize = size;
            Changed?.Invoke();
        }

        /// <summary>Sets the view rotation (bearing of viewport up).</summary>
        public void SetRotation(double degrees) {
            if (degrees.Equals(_rotationDegrees)) return;

            _rotationDegrees = degrees;
            Changed?.Invoke();
        }

        /// <summary>Limits the centre to <paramref name="bounds"/> (null removes the limit) and re-clamps it.</summary>
        public void SetPanBounds(MapRect? bounds) {
            _panBounds = bounds;
            _centre = ClampCentre(_centre);
            Changed?.Invoke();
        }

        /// <summary>Sets the zoom, clamped to the limits.</summary>
        public void SetMetresAcross(double metresAcross) {
            double clamped = ClampZoom(metresAcross);
            if (clamped.Equals(_metresAcross)) return;

            _metresAcross = clamped;
            Changed?.Invoke();
        }

        /// <summary>Moves the centre to <paramref name="centre"/> (clamped to the pan bounds).</summary>
        public void Recentre(MapPoint centre) {
            _centre = ClampCentre(centre);
            Changed?.Invoke();
        }

        /// <summary>Drags the map content by <paramref name="deltaViewportUnits"/>: the point under the pointer follows it.</summary>
        public void Pan(Vector2 deltaViewportUnits) {
            if (deltaViewportUnits == Vector2.zero) return;

            MapPoint shift = ViewportDeltaToWorld(deltaViewportUnits.x, deltaViewportUnits.y);
            _centre = ClampCentre(_centre - shift);
            Changed?.Invoke();
        }

        /// <summary>
        /// Multiplies the metres across by <paramref name="factor"/> (below 1 zooms in), keeping the map
        /// point under <paramref name="viewportPoint"/> fixed. A no-op at the zoom limit.
        /// </summary>
        public void ZoomAbout(Vector2 viewportPoint, double factor) {
            if (factor <= 0.0 || double.IsNaN(factor)) return;

            double zoomed = ClampZoom(_metresAcross * factor);
            if (zoomed.Equals(_metresAcross)) return;

            MapPoint anchor = ViewportToWorld(viewportPoint);
            _metresAcross = zoomed;
            MapPoint offset = ViewportDeltaToWorld(viewportPoint.x, viewportPoint.y);
            _centre = ClampCentre(anchor - offset);
            Changed?.Invoke();
        }

        /// <summary>Viewport point (centre-relative canvas units) where <paramref name="point"/> is drawn.</summary>
        public Vector2 WorldToViewport(MapPoint point) {
            double deltaX = point.X - _centre.X;
            double deltaZ = point.Z - _centre.Z;
            double radians = _rotationDegrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            double metresPerUnit = MetresPerUnit;
            double viewportX = (deltaX * cosine - deltaZ * sine) / metresPerUnit;
            double viewportY = (deltaX * sine + deltaZ * cosine) / metresPerUnit;
            return new Vector2((float)viewportX, (float)viewportY);
        }

        /// <summary>Map point drawn at <paramref name="viewportPoint"/> (centre-relative canvas units).</summary>
        public MapPoint ViewportToWorld(Vector2 viewportPoint) {
            return _centre + ViewportDeltaToWorld(viewportPoint.x, viewportPoint.y);
        }

        /// <summary>
        /// Cells (grid of <paramref name="cellSize"/>, cell (0,0) at [0, size)) touched by the rotated
        /// viewport, grown by <paramref name="margin"/> cells.
        /// </summary>
        public GridRect VisibleCells(double cellSize, int margin) {
            double halfWidth = _viewportSize.x * 0.5;
            double halfHeight = _viewportSize.y * 0.5;
            double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
            foreach (int signX in new[] { -1, 1 }) {
                foreach (int signY in new[] { -1, 1 }) {
                    MapPoint corner = ViewportToWorld(new Vector2((float)(signX * halfWidth), (float)(signY * halfHeight)));
                    minX = Math.Min(minX, corner.X);
                    minZ = Math.Min(minZ, corner.Z);
                    maxX = Math.Max(maxX, corner.X);
                    maxZ = Math.Max(maxZ, corner.Z);
                }
            }

            var min = new CellCoord((int)Math.Floor(minX / cellSize) - margin, (int)Math.Floor(minZ / cellSize) - margin);
            var max = new CellCoord((int)Math.Floor(maxX / cellSize) + margin, (int)Math.Floor(maxZ / cellSize) + margin);
            return GridRect.FromCorners(min, max);
        }

        private MapPoint ViewportDeltaToWorld(double viewportX, double viewportY) {
            double radians = _rotationDegrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            double metresPerUnit = MetresPerUnit;
            double deltaX = (viewportX * cosine + viewportY * sine) * metresPerUnit;
            double deltaZ = (-viewportX * sine + viewportY * cosine) * metresPerUnit;
            return new MapPoint(deltaX, deltaZ);
        }

        private double ClampZoom(double metresAcross) {
            return Math.Min(Math.Max(metresAcross, MinMetresAcross), MaxMetresAcross);
        }

        private MapPoint ClampCentre(MapPoint centre) {
            return _panBounds.HasValue ? _panBounds.Value.Clamp(centre) : centre;
        }
    }
}
