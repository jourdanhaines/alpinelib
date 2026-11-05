using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// A moving blocker's box in its own frame (a train car's), grown from its parts' local boxes mapped into that frame:
    /// a part turned with the car adds only its own extent, never a world-axis box around it, however the car is
    /// heading. A ball touches the blocker when its centre, mapped into the frame, lies in the box grown by its radius.
    /// </summary>
    public sealed class RollerBlockerBox {
        public bool IsEmpty { get; private set; } = true;

        public double MinX { get; private set; }
        public double MinY { get; private set; }
        public double MinZ { get; private set; }
        public double MaxX { get; private set; }
        public double MaxY { get; private set; }
        public double MaxZ { get; private set; }

        public void Clear() {
            IsEmpty = true;
            MinX = MinY = MinZ = MaxX = MaxY = MaxZ = 0.0;
        }

        /// <summary>Grows the box by a part's local box (centre, half extents) mapped by <paramref name="partToFrame"/>.</summary>
        public void AddPart(double centreX, double centreY, double centreZ, double extentX, double extentY, double extentZ, RollerAffine partToFrame) {
            for (int corner = 0; corner < 8; corner++) {
                double x = centreX + ((corner & 1) == 0 ? -extentX : extentX);
                double y = centreY + ((corner & 2) == 0 ? -extentY : extentY);
                double z = centreZ + ((corner & 4) == 0 ? -extentZ : extentZ);
                partToFrame.Apply(x, y, z, out double frameX, out double frameY, out double frameZ);
                Encapsulate(frameX, frameY, frameZ);
            }
        }

        /// <summary>True when a ball of <paramref name="radius"/> centred at frame point (x, y, z) touches the box.</summary>
        public bool Touches(double x, double y, double z, double radius) {
            if (IsEmpty) return false;

            return x > MinX - radius && x < MaxX + radius && y > MinY - radius && y < MaxY + radius && z > MinZ - radius && z < MaxZ + radius;
        }

        private void Encapsulate(double x, double y, double z) {
            if (IsEmpty) {
                MinX = MaxX = x;
                MinY = MaxY = y;
                MinZ = MaxZ = z;
                IsEmpty = false;
                return;
            }

            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MinZ = Math.Min(MinZ, z);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
            MaxZ = Math.Max(MaxZ, z);
        }
    }
}
