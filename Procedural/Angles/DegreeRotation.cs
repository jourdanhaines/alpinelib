namespace AlpineLib.Procedural {
    /// <summary>
    /// A whole-degree rotation as Unity applies <c>Quaternion.Euler(pitch, yaw, roll)</c>: roll about local +Z, then
    /// pitch about +X, then yaw about +Y (R = Ry · Rx · Rz, column vectors), from <see cref="DegreeSine"/> (no runtime
    /// trigonometry), so every runtime turns points to the same bits. <c>default</c> is the identity.
    /// </summary>
    public readonly struct DegreeRotation {
        private readonly double _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;
        // False only for default(DegreeRotation), whose zero matrix stands for the identity.
        private readonly bool _isBuilt;

        /// <summary>The rotation of yaw, pitch and roll in whole degrees.</summary>
        public DegreeRotation(int yawDegrees, int pitchDegrees, int rollDegrees) {
            DegreeSine.SinCos(yawDegrees, out double yawSine, out double yawCosine);
            DegreeSine.SinCos(pitchDegrees, out double pitchSine, out double pitchCosine);
            DegreeSine.SinCos(rollDegrees, out double rollSine, out double rollCosine);
            // Rx · Rz first, then Ry on the left.
            double a00 = rollCosine, a01 = -rollSine, a02 = 0.0;
            double a10 = pitchCosine * rollSine, a11 = pitchCosine * rollCosine, a12 = -pitchSine;
            double a20 = pitchSine * rollSine, a21 = pitchSine * rollCosine, a22 = pitchCosine;
            _m00 = yawCosine * a00 + yawSine * a20;
            _m01 = yawCosine * a01 + yawSine * a21;
            _m02 = yawCosine * a02 + yawSine * a22;
            _m10 = a10;
            _m11 = a11;
            _m12 = a12;
            _m20 = -yawSine * a00 + yawCosine * a20;
            _m21 = -yawSine * a01 + yawCosine * a21;
            _m22 = -yawSine * a02 + yawCosine * a22;
            _isBuilt = true;
        }

        /// <summary>The local vector (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) turned by the rotation.</summary>
        public void Apply(double x, double y, double z, out double outX, out double outY, out double outZ) {
            if (!_isBuilt) {
                (outX, outY, outZ) = (x, y, z);
                return;
            }

            outX = _m00 * x + _m01 * y + _m02 * z;
            outY = _m10 * x + _m11 * y + _m12 * z;
            outZ = _m20 * x + _m21 * y + _m22 * z;
        }

        /// <summary>
        /// The half extents along the parent's axes of a box of half extents (<paramref name="halfX"/>, <paramref name="halfY"/>,
        /// <paramref name="halfZ"/>) turned by the rotation.
        /// </summary>
        public void Extents(double halfX, double halfY, double halfZ, out double extentX, out double extentY, out double extentZ) {
            if (!_isBuilt) {
                (extentX, extentY, extentZ) = (halfX, halfY, halfZ);
                return;
            }

            extentX = Abs(_m00) * halfX + Abs(_m01) * halfY + Abs(_m02) * halfZ;
            extentY = Abs(_m10) * halfX + Abs(_m11) * halfY + Abs(_m12) * halfZ;
            extentZ = Abs(_m20) * halfX + Abs(_m21) * halfY + Abs(_m22) * halfZ;
        }

        /// <summary>
        /// The upward half extent of an ellipsoid of semi-axes (<paramref name="halfX"/>, <paramref name="halfY"/>,
        /// <paramref name="halfZ"/>) turned by the rotation: a rounded rock's height, closer to its mesh than the box's.
        /// </summary>
        public double RoundedExtentY(double halfX, double halfY, double halfZ) {
            if (!_isBuilt) return System.Math.Sqrt(halfY * halfY);

            double fromX = _m10 * halfX, fromY = _m11 * halfY, fromZ = _m12 * halfZ;
            return System.Math.Sqrt(fromX * fromX + fromY * fromY + fromZ * fromZ);
        }

        private static double Abs(double value) {
            return value < 0.0 ? -value : value;
        }
    }
}
