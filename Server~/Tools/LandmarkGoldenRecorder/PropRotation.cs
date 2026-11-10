using AllAboard.GoldenRecorder;
using AlpineLib.Procedural;

namespace AllAboard.WorldGen {
    /// <summary>Facade over the game's prop rotation: keeps its angles and checks every operation against <see cref="DegreeRotation"/>.</summary>
    public readonly struct PropRotation {
        public PropRotation(int yawDegrees, int pitchDegrees, int rollDegrees) {
            Game = new PropRotationGame(yawDegrees, pitchDegrees, rollDegrees);
            Yaw = yawDegrees;
            Pitch = pitchDegrees;
            Roll = rollDegrees;
        }

        public PropRotationGame Game { get; }

        public int Yaw { get; }

        public int Pitch { get; }

        public int Roll { get; }

        public DegreeRotation Lib => new DegreeRotation(Yaw, Pitch, Roll);

        public void Apply(double x, double y, double z, out double outX, out double outY, out double outZ) {
            Game.Apply(x, y, z, out outX, out outY, out outZ);
            Lib.Apply(x, y, z, out double libX, out double libY, out double libZ);
            Recorder.Check("rotation.apply", Recorder.Same(outX, libX) && Recorder.Same(outY, libY) && Recorder.Same(outZ, libZ));
        }

        public void Extents(double halfX, double halfY, double halfZ, out double extentX, out double extentY, out double extentZ) {
            Game.Extents(halfX, halfY, halfZ, out extentX, out extentY, out extentZ);
            Lib.Extents(halfX, halfY, halfZ, out double libX, out double libY, out double libZ);
            Recorder.Check("rotation.extents", Recorder.Same(extentX, libX) && Recorder.Same(extentY, libY) && Recorder.Same(extentZ, libZ));
        }

        public double RoundedExtentY(double halfX, double halfY, double halfZ) {
            double game = Game.RoundedExtentY(halfX, halfY, halfZ);
            Recorder.Check("rotation.rounded", Recorder.Same(game, Lib.RoundedExtentY(halfX, halfY, halfZ)));
            return game;
        }
    }
}
