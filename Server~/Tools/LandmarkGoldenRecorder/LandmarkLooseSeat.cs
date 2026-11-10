using System.Collections.Generic;
using AllAboard.GoldenRecorder;

namespace AllAboard.WorldGen {
    /// <summary>Facade over the game's rock seating: returns the game's results, checking every call against the lib.</summary>
    public static class LandmarkLooseSeat {
        public const double LowPointSlack = LandmarkLooseSeatGame.LowPointSlack;

        public static double Surface(IReadOnlyList<LandmarkSlopePiece> slopes, IReadOnlyList<LandmarkPrism> plinths, double x, double z) {
            return LandmarkLooseSeatGame.Surface(slopes, plinths, x, z);
        }

        public static List<LandmarkPrism> Plinths(IReadOnlyList<LandmarkPrism> prisms) => LandmarkLooseSeatGame.Plinths(prisms);

        public static double Seated(PropRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, IReadOnlyList<LandmarkSlopePiece> slopes,
                                    IReadOnlyList<LandmarkPrism> plinths) {
            double game = LandmarkLooseSeatGame.Seated(rotation.Game, x, centreY, z, halfX, halfY, halfZ, slopes, plinths);
            SeatRecorder.CheckSeated(rotation, x, centreY, z, halfX, halfY, halfZ, slopes, plinths, game);
            return game;
        }

        public static double Buried(PropRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, IReadOnlyList<LandmarkSlopePiece> slopes,
                                    IReadOnlyList<LandmarkPrism> plinths) {
            double game = LandmarkLooseSeatGame.Buried(rotation.Game, x, centreY, z, halfX, halfY, halfZ, slopes, plinths);
            SeatRecorder.CheckBuried(rotation, x, centreY, z, halfX, halfY, halfZ, slopes, plinths, game);
            return game;
        }
    }
}
