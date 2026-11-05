namespace AlpineLib.Procedural.Sky {
    /// <summary>A 3 × 4 affine map (rotation, scale and translation) between two frames, row-major: x' = M00 x + M01 y + M02 z + M03.</summary>
    public readonly struct RollerAffine {
        public RollerAffine(double m00, double m01, double m02, double m03, double m10, double m11, double m12, double m13, double m20, double m21, double m22, double m23) {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M03 = m03;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M20 = m20;
            M21 = m21;
            M22 = m22;
            M23 = m23;
        }

        public double M00 { get; }
        public double M01 { get; }
        public double M02 { get; }
        public double M03 { get; }
        public double M10 { get; }
        public double M11 { get; }
        public double M12 { get; }
        public double M13 { get; }
        public double M20 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double M23 { get; }

        public static RollerAffine Identity => new RollerAffine(1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0);

        /// <summary>A turn of <paramref name="yaw"/> radians about up (clockwise seen from above) then a move by (x, y, z).</summary>
        public static RollerAffine YawThenMove(double yaw, double x, double y, double z) {
            double cos = System.Math.Cos(yaw), sin = System.Math.Sin(yaw);
            return new RollerAffine(cos, 0.0, sin, x, 0.0, 1.0, 0.0, y, -sin, 0.0, cos, z);
        }

        /// <summary>This map after <paramref name="first"/>: a point goes through <paramref name="first"/>, then this.</summary>
        public RollerAffine After(RollerAffine first) {
            return new RollerAffine(
                M00 * first.M00 + M01 * first.M10 + M02 * first.M20, M00 * first.M01 + M01 * first.M11 + M02 * first.M21,
                M00 * first.M02 + M01 * first.M12 + M02 * first.M22, M00 * first.M03 + M01 * first.M13 + M02 * first.M23 + M03,
                M10 * first.M00 + M11 * first.M10 + M12 * first.M20, M10 * first.M01 + M11 * first.M11 + M12 * first.M21,
                M10 * first.M02 + M11 * first.M12 + M12 * first.M22, M10 * first.M03 + M11 * first.M13 + M12 * first.M23 + M13,
                M20 * first.M00 + M21 * first.M10 + M22 * first.M20, M20 * first.M01 + M21 * first.M11 + M22 * first.M21,
                M20 * first.M02 + M21 * first.M12 + M22 * first.M22, M20 * first.M03 + M21 * first.M13 + M22 * first.M23 + M23);
        }

        /// <summary>The map back (the linear part must not be singular).</summary>
        public RollerAffine Inverse() {
            double c00 = M11 * M22 - M12 * M21, c01 = M02 * M21 - M01 * M22, c02 = M01 * M12 - M02 * M11;
            double c10 = M12 * M20 - M10 * M22, c11 = M00 * M22 - M02 * M20, c12 = M02 * M10 - M00 * M12;
            double c20 = M10 * M21 - M11 * M20, c21 = M01 * M20 - M00 * M21, c22 = M00 * M11 - M01 * M10;
            double scale = 1.0 / (M00 * c00 + M01 * c10 + M02 * c20);
            double i00 = c00 * scale, i01 = c01 * scale, i02 = c02 * scale;
            double i10 = c10 * scale, i11 = c11 * scale, i12 = c12 * scale;
            double i20 = c20 * scale, i21 = c21 * scale, i22 = c22 * scale;
            return new RollerAffine(i00, i01, i02, -(i00 * M03 + i01 * M13 + i02 * M23), i10, i11, i12, -(i10 * M03 + i11 * M13 + i12 * M23),
                i20, i21, i22, -(i20 * M03 + i21 * M13 + i22 * M23));
        }

        public void Apply(double x, double y, double z, out double mappedX, out double mappedY, out double mappedZ) {
            mappedX = M00 * x + M01 * y + M02 * z + M03;
            mappedY = M10 * x + M11 * y + M12 * z + M13;
            mappedZ = M20 * x + M21 * y + M22 * z + M23;
        }
    }
}
