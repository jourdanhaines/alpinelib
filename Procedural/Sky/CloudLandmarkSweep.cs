using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Engine-free checks of a landmark-aware cloud field (gates and tests): whether any drawn cloud's footprint overlaps
    /// a landmark that blocks it, how fast clouds dissolve, and how much denser the sky is in giants' zones than in open
    /// sky. Places clouds exactly as the runtime field does (anchor, class drift, smoothed wander and surge, then
    /// envelope, size boost and dissolve at the cloud's absolute position).
    /// </summary>
    public sealed class CloudLandmarkSweep {
        private const int MaxCachedCells = 20000;

        private readonly CloudFieldScatter _scatter;
        private readonly WindField _wind;
        private readonly CloudLandmarkInfluence _influence;
        private readonly Dictionary<(CloudClass Class, long X, long Z), CloudInstance[]> _cells = new Dictionary<(CloudClass Class, long X, long Z), CloudInstance[]>();

        /// <summary>A sweep of <paramref name="scatter"/> (with <see cref="CloudFieldSettings.Landmarks"/> set) drifting on <paramref name="wind"/>.</summary>
        public CloudLandmarkSweep(CloudFieldScatter scatter, WindField wind) {
            _scatter = scatter ?? throw new ArgumentNullException(nameof(scatter));
            _wind = wind ?? throw new ArgumentNullException(nameof(wind));
            if (scatter.Settings.Landmarks == null) throw new ArgumentException("The scatter ignores landmarks.", nameof(scatter));

            _influence = new CloudLandmarkInfluence(scatter.Settings.Landmarks);
        }

        public CloudLandmarkInfluence Influence => _influence;

        /// <summary>
        /// Every cloud of every class near one of <paramref name="shapes"/>, sampled every <paramref name="step"/> seconds
        /// from <paramref name="start"/> for <paramref name="seconds"/>: drawn clouds (dissolve share at least the cutoff)
        /// whose drawn footprint overlaps a hull that blocks them, and the fastest change of the dissolve share per second
        /// (each sample against the same cloud <paramref name="rateStep"/> seconds on).
        /// </summary>
        public CloudDissolveCheck Dissolve(IReadOnlyList<LandmarkSkyShape> shapes, double start, double seconds, double step, double rateStep) {
            var result = new CloudDissolveCheck();
            if (shapes == null || shapes.Count == 0) return result;

            for (double time = start; time <= start + seconds + 1e-9; time += step) {
                foreach (LandmarkSkyShape shape in shapes) {
                    for (int classIndex = 0; classIndex < CloudFieldSettings.ClassCount; classIndex++) SweepShape(shapes, shape, (CloudClass)classIndex, time, rateStep, result);
                }
            }
            return result;
        }

        /// <summary>
        /// Clouds per km² (each counted by its envelope) where the giant bias is at full strength and where it is none,
        /// within <paramref name="radius"/> of (<paramref name="centreX"/>, <paramref name="centreZ"/>) at <paramref name="time"/>;
        /// areas from a grid of <paramref name="spacing"/> metres.
        /// </summary>
        public CloudDensity Density(IReadOnlyList<LandmarkSkyShape> shapes, double centreX, double centreZ, double radius, double time, double spacing) {
            var result = new CloudDensity();
            WindVector heading = _wind.Direction(time);
            WindVector displacement = _wind.Displacement(time);
            MeasureAreas(shapes, centreX, centreZ, radius, heading, spacing, result);
            for (int classIndex = 0; classIndex < CloudFieldSettings.ClassCount; classIndex++) CountClass(shapes, (CloudClass)classIndex, centreX, centreZ, radius, time, heading, displacement, result);
            return result;
        }

        /// <summary>True when <paramref name="bias"/> is the giant bias at full strength.</summary>
        public bool IsFullZone(double bias) {
            return bias >= _influence.Settings.GiantBias * (1.0 - 1e-9);
        }

        private void SweepShape(IReadOnlyList<LandmarkSkyShape> shapes, LandmarkSkyShape shape, CloudClass cloudClass, double time, double rateStep, CloudDissolveCheck result) {
            CloudFieldSettings settings = _scatter.Settings;
            CloudClassSettings classSettings = settings.ClassOf(cloudClass);
            double reach = shape.BoundRadius + settings.MaxReach(classSettings) * _scatter.LargestBoost(cloudClass) + classSettings.MotionReach + _influence.Settings.DissolveFar;
            CloudFieldFrame frame = CloudFieldFrame.Locate(shape.BoundX, shape.BoundZ, _scatter.DriftOf(cloudClass, _wind.Displacement(time)), settings.CellSize);
            int cells = (int)Math.Ceiling(reach / settings.CellSize) + 1;
            for (long cellZ = frame.CellZ - cells; cellZ <= frame.CellZ + cells; cellZ++) {
                for (long cellX = frame.CellX - cells; cellX <= frame.CellX + cells; cellX++) SweepCell(shapes, shape, cloudClass, frame, cellX, cellZ, time, rateStep, result);
            }
        }

        private void SweepCell(IReadOnlyList<LandmarkSkyShape> shapes, LandmarkSkyShape shape, CloudClass cloudClass, CloudFieldFrame frame, long cellX, long cellZ, double time, double rateStep, CloudDissolveCheck result) {
            if (shape.PlateauY + _influence.Settings.BlockMargin <= _scatter.Settings.ClassOf(cloudClass).MinAltitude) return;

            CloudFieldFrame later = CloudFieldFrame.Locate(shape.BoundX, shape.BoundZ, _scatter.DriftOf(cloudClass, _wind.Displacement(time + rateStep)), _scatter.Settings.CellSize);
            foreach (CloudInstance cloud in CellOf(cloudClass, cellX, cellZ)) {
                Place(cloud, frame, cellX, cellZ, shape.BoundX, shape.BoundZ, time, out double x, out double z);
                double share = DrawnShare(shapes, cloud, x, z, time, out double drawn);
                Place(cloud, later, cellX, cellZ, shape.BoundX, shape.BoundZ, time + rateStep, out double laterX, out double laterZ);
                double laterShare = DrawnShare(shapes, cloud, laterX, laterZ, time + rateStep, out double _);
                result.AddRate(Math.Abs(laterShare - share) / rateStep);
                if (drawn <= 0.0) continue;

                result.AddDrawn(OverlapsBlocking(shapes, cloud.Altitude, x, z, drawn));
            }
        }

        // The cloud's dissolve share at its place and time, and its drawn footprint radius (0 when it is not drawn).
        private double DrawnShare(IReadOnlyList<LandmarkSkyShape> shapes, in CloudInstance cloud, double x, double z, double time, out double drawn) {
            double bias = _influence.GiantBias(shapes, x, z, _wind.Direction(time));
            double envelope = _scatter.Envelope(cloud, bias);
            double share = _influence.Dissolve(shapes, x, z, cloud.Reach * _scatter.LargestBoost(cloud.Class), cloud.Altitude);
            bool isDrawn = envelope > 0.0 && !_influence.IsDissolved(share);
            drawn = isDrawn ? cloud.Reach * _scatter.SizeBoost(cloud, bias) * envelope * share : 0.0;
            return share;
        }

        private bool OverlapsBlocking(IReadOnlyList<LandmarkSkyShape> shapes, double altitude, double x, double z, double drawn) {
            foreach (LandmarkSkyShape blocking in shapes) {
                if (blocking.PlateauY + _influence.Settings.BlockMargin > altitude && blocking.DistanceTo(x, z) < drawn) return true;
            }
            return false;
        }

        private void CountClass(IReadOnlyList<LandmarkSkyShape> shapes, CloudClass cloudClass, double centreX, double centreZ, double radius, double time, WindVector heading, WindVector displacement, CloudDensity result) {
            CloudFieldSettings settings = _scatter.Settings;
            CloudFieldFrame frame = CloudFieldFrame.Locate(centreX, centreZ, _scatter.DriftOf(cloudClass, displacement), settings.CellSize);
            int cells = (int)Math.Ceiling(radius / settings.CellSize) + 1;
            for (long cellZ = frame.CellZ - cells; cellZ <= frame.CellZ + cells; cellZ++) {
                for (long cellX = frame.CellX - cells; cellX <= frame.CellX + cells; cellX++) CountCell(shapes, cloudClass, frame, cellX, cellZ, centreX, centreZ, radius, time, heading, result);
            }
        }

        private void CountCell(IReadOnlyList<LandmarkSkyShape> shapes, CloudClass cloudClass, CloudFieldFrame frame, long cellX, long cellZ, double centreX, double centreZ, double radius, double time, WindVector heading, CloudDensity result) {
            foreach (CloudInstance cloud in CellOf(cloudClass, cellX, cellZ)) {
                Place(cloud, frame, cellX, cellZ, centreX, centreZ, time, out double x, out double z);
                double deltaX = x - centreX;
                double deltaZ = z - centreZ;
                if (deltaX * deltaX + deltaZ * deltaZ > radius * radius) continue;

                double bias = _influence.GiantBias(shapes, x, z, heading);
                double envelope = _scatter.Envelope(cloud, bias);
                double drawn = cloud.Reach * _scatter.SizeBoost(cloud, bias) * envelope;
                double cover = Math.PI * drawn * drawn;
                if (IsFullZone(bias)) {
                    result.ZoneClouds += envelope;
                    result.ZoneCover += cover;
                } else if (bias <= 0.0) {
                    result.OpenClouds += envelope;
                    result.OpenCover += cover;
                }
            }
        }

        private void MeasureAreas(IReadOnlyList<LandmarkSkyShape> shapes, double centreX, double centreZ, double radius, WindVector heading, double spacing, CloudDensity result) {
            double cellArea = spacing * spacing / 1e6;
            int steps = (int)Math.Ceiling(radius / spacing);
            for (int stepZ = -steps; stepZ <= steps; stepZ++) {
                for (int stepX = -steps; stepX <= steps; stepX++) {
                    double offsetX = stepX * spacing;
                    double offsetZ = stepZ * spacing;
                    if (offsetX * offsetX + offsetZ * offsetZ > radius * radius) continue;

                    double bias = _influence.GiantBias(shapes, centreX + offsetX, centreZ + offsetZ, heading);
                    if (IsFullZone(bias)) result.ZoneArea += cellArea;
                    else if (bias <= 0.0) result.OpenArea += cellArea;
                }
            }
        }

        // Cells are pure data, cached across samples (cleared past a bound so long sweeps stay small).
        private CloudInstance[] CellOf(CloudClass cloudClass, long cellX, long cellZ) {
            if (_cells.TryGetValue((cloudClass, cellX, cellZ), out CloudInstance[] clouds)) return clouds;
            if (_cells.Count > MaxCachedCells) _cells.Clear();

            clouds = _scatter.Cell(cloudClass, cellX, cellZ);
            _cells.Add((cloudClass, cellX, cellZ), clouds);
            return clouds;
        }

        // Absolute centre of a cloud seen from a camera at (cameraX, cameraZ), exactly as the runtime field places it.
        private void Place(in CloudInstance cloud, CloudFieldFrame frame, long cellX, long cellZ, double cameraX, double cameraZ, double time, out double x, out double z) {
            _scatter.SmoothOffset(cloud, time, _wind, out double offsetX, out double offsetZ);
            x = cameraX + frame.OffsetX(cellX, cloud.AnchorX) + offsetX;
            z = cameraZ + frame.OffsetZ(cellZ, cloud.AnchorZ) + offsetZ;
        }
    }
}
