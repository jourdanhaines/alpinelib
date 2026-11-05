using System;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Server.Tests.Sky {
    // A flat test cell with one stop disc at local (x, z) (x NaN: none).
    public sealed class RollerStopCell : IRollerCell {
        private readonly double _x;
        private readonly double _z;
        private readonly double _radius;

        public RollerStopCell(double x, double z, double radius) {
            _x = x;
            _z = z;
            _radius = radius;
        }

        public RollerBlock BlockAt(double x, double z, double radius) {
            if (double.IsNaN(_x)) return RollerBlock.None;

            double dx = x - _x, dz = z - _z, reach = _radius + radius;
            return dx * dx + dz * dz < reach * reach ? RollerBlock.Stop : RollerBlock.None;
        }

        public RollerSupport SupportAt(double x, double z, double radius) {
            return RollerSupport.Level(0.0);
        }

        public double SurfaceAt(double x, double z) {
            return 0.0;
        }
    }
}
