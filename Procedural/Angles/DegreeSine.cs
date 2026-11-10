namespace AlpineLib.Procedural {
    /// <summary>
    /// Sine and cosine of whole degrees from a literal table rounded to 1e-7, so no runtime evaluates a
    /// trigonometric function and every runtime gets the same bits. Any integer degree wraps into 0–359.
    /// </summary>
    public static class DegreeSine {
        // sin(0°..90°) rounded to 1e-7.
        private static readonly double[] QuarterSine = {
            0.0, 0.0174524, 0.0348995, 0.052336, 0.0697565, 0.0871557, 0.1045285, 0.1218693, 0.1391731, 0.1564345,
            0.1736482, 0.190809, 0.2079117, 0.2249511, 0.2419219, 0.258819, 0.2756374, 0.2923717, 0.309017, 0.3255682,
            0.3420201, 0.3583679, 0.3746066, 0.3907311, 0.4067366, 0.4226183, 0.4383711, 0.4539905, 0.4694716, 0.4848096,
            0.5, 0.5150381, 0.5299193, 0.544639, 0.5591929, 0.5735764, 0.5877853, 0.601815, 0.6156615, 0.6293204,
            0.6427876, 0.656059, 0.6691306, 0.6819984, 0.6946584, 0.7071068, 0.7193398, 0.7313537, 0.7431448, 0.7547096,
            0.7660444, 0.777146, 0.7880108, 0.7986355, 0.809017, 0.819152, 0.8290376, 0.8386706, 0.8480481, 0.8571673,
            0.8660254, 0.8746197, 0.8829476, 0.8910065, 0.898794, 0.9063078, 0.9135455, 0.9205049, 0.9271839, 0.9335804,
            0.9396926, 0.9455186, 0.9510565, 0.9563048, 0.9612617, 0.9659258, 0.9702957, 0.9743701, 0.9781476, 0.9816272,
            0.9848078, 0.9876883, 0.9902681, 0.9925462, 0.9945219, 0.9961947, 0.9975641, 0.9986295, 0.9993908, 0.9998477,
            1.0,
        };

        /// <summary>Sine and cosine of whole <paramref name="degrees"/>; negative and past-a-turn degrees wrap.</summary>
        public static void SinCos(int degrees, out double sine, out double cosine) {
            int wrapped = ((degrees % 360) + 360) % 360;
            sine = TableSine(wrapped);
            cosine = TableSine((wrapped + 90) % 360);
        }

        private static double TableSine(int degrees) {
            if (degrees <= 90) return QuarterSine[degrees];
            if (degrees <= 180) return QuarterSine[180 - degrees];
            if (degrees <= 270) return -QuarterSine[degrees - 180];
            return -QuarterSine[360 - degrees];
        }
    }
}
