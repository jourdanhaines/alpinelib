using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Server.Tests.Procedural.Kits {
    /// <summary>One recorded rock seating: the rock, the surface pieces near it and the game's seated height and burial.</summary>
    public sealed class SeatCase {
        public SeatCase(int yaw, int pitch, int roll, double x, double centreY, double z, double halfX, double halfY, double halfZ, List<SlopedPrismPiece> slopes,
                        List<(List<PlanPoint> Plan, double Top)> plinths, double seated, double buried) {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
            X = x;
            CentreY = centreY;
            Z = z;
            HalfX = halfX;
            HalfY = halfY;
            HalfZ = halfZ;
            Slopes = slopes;
            Plinths = plinths;
            Seated = seated;
            Buried = buried;
        }

        public int Yaw { get; }
        public int Pitch { get; }
        public int Roll { get; }
        public double X { get; }
        public double CentreY { get; }
        public double Z { get; }
        public double HalfX { get; }
        public double HalfY { get; }
        public double HalfZ { get; }
        public List<SlopedPrismPiece> Slopes { get; }

        /// <summary>Flat plinth tops, or null when the game passed none.</summary>
        public List<(List<PlanPoint> Plan, double Top)> Plinths { get; }

        /// <summary>The game's seated centre height.</summary>
        public double Seated { get; }

        /// <summary>The game's burial share at the seated height.</summary>
        public double Buried { get; }
    }
}
