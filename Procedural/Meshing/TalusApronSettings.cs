using System;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>The shape rules of a <see cref="TalusApron"/>; every length in metres.</summary>
    public readonly struct TalusApronSettings {
        /// <summary>Builds the rules; see each property.</summary>
        public TalusApronSettings(double innerDepth, double toeSink, double gradientMax, double fanGradientMargin, int minFanSteps, int maxFanSteps, double exposureProbe,
                                  double minInterval, double keepOffGap, double seamStep) {
            if (!(innerDepth >= 0.0)) throw new ArgumentOutOfRangeException(nameof(innerDepth), innerDepth, "Inner depth must not be negative.");
            if (!(toeSink >= 0.0)) throw new ArgumentOutOfRangeException(nameof(toeSink), toeSink, "Toe sink must not be negative.");
            if (!(gradientMax > 0.0)) throw new ArgumentOutOfRangeException(nameof(gradientMax), gradientMax, "Gradient cap must be positive.");
            if (!(fanGradientMargin > 0.0)) throw new ArgumentOutOfRangeException(nameof(fanGradientMargin), fanGradientMargin, "Fan margin must be positive.");
            if (minFanSteps < 1 || maxFanSteps < minFanSteps) throw new ArgumentOutOfRangeException(nameof(maxFanSteps), maxFanSteps, "Fan steps need 1 ≤ min ≤ max.");
            if (!(exposureProbe > 0.0)) throw new ArgumentOutOfRangeException(nameof(exposureProbe), exposureProbe, "Exposure probe must be positive.");
            if (!(minInterval > 0.0)) throw new ArgumentOutOfRangeException(nameof(minInterval), minInterval, "Shortest run must be positive.");
            if (!(keepOffGap >= 0.0)) throw new ArgumentOutOfRangeException(nameof(keepOffGap), keepOffGap, "Keep-off gap must not be negative.");
            if (!(seamStep >= 0.0)) throw new ArgumentOutOfRangeException(nameof(seamStep), seamStep, "Seam step must not be negative.");

            InnerDepth = innerDepth;
            ToeSink = toeSink;
            GradientMax = gradientMax;
            FanGradientMargin = fanGradientMargin;
            MinFanSteps = minFanSteps;
            MaxFanSteps = maxFanSteps;
            ExposureProbe = exposureProbe;
            MinInterval = minInterval;
            KeepOffGap = keepOffGap;
            SeamStep = seamStep;
            IsBuilt = true;
        }

        /// <summary>How far a strip reaches into the rock past the wall foot.</summary>
        public double InnerDepth { get; }

        /// <summary>How far under the ground the slope reaches at the toe (the apron is (rise + sink) / slope wide).</summary>
        public double ToeSink { get; }

        /// <summary>The steepest rise per metre a fan facet may take.</summary>
        public double GradientMax { get; }

        /// <summary>Factor on slope / <see cref="GradientMax"/> when sizing fan steps (above 1 lets facets run a little past the cap).</summary>
        public double FanGradientMargin { get; }

        /// <summary>Fewest triangles in a fan.</summary>
        public int MinFanSteps { get; }

        /// <summary>Most triangles in a fan; a fan needing more fails like an open one.</summary>
        public int MaxFanSteps { get; }

        /// <summary>How far out from the foot an edge is probed against other footprints for exposure.</summary>
        public double ExposureProbe { get; }

        /// <summary>Shortest foot edge or exposed stretch that takes an apron.</summary>
        public double MinInterval { get; }

        /// <summary>Foot stretches within this of a keep-off region stay bare.</summary>
        public double KeepOffGap { get; }

        /// <summary>Two pieces sharing an edge must agree on its heights within this, else the apron is not laid.</summary>
        public double SeamStep { get; }

        /// <summary>False for <c>default</c> settings, which <see cref="TalusApron.Lay"/> refuses.</summary>
        public bool IsBuilt { get; }
    }
}
