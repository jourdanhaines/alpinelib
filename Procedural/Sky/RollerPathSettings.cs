using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of wind-rolled props (tumbleweeds): how far one rolls before it breaks up, how its heading wanders off
    /// the prevailing wind (noise wander plus seeded course changes), when it jumps (obstacles, crests, seeded bumps),
    /// how fast it rolls for the wind's drift, how long it fades in and out, and how much props vary in size, speed and
    /// shape (small ones common and fast, big ones rare and slow).
    /// </summary>
    /// <remarks>Distances are ground metres along the roll unless named otherwise.</remarks>
    public sealed class RollerPathSettings {
        /// <summary>Most a roll's heading may stray off the prevailing wind, degrees (wander and course changes together).</summary>
        public const double MaxOffWindDegrees = 80.0;

        /// <summary>Range of how far a prop rolls before it breaks up (fades out), metres.</summary>
        public double MinLife { get; set; } = 60.0;
        public double MaxLife { get; set; } = 180.0;

        /// <summary>Shortest roll kept; a spawn blocked sooner gets no roller.</summary>
        public double MinClippedLength { get; set; } = 8.0;

        /// <summary>Most the heading strays either side of the prevailing wind, degrees: the noise wander and course changes are clamped to it.</summary>
        public double WanderDegrees { get; set; } = 35.0;

        /// <summary>Gain on the wander noise (fBm rarely reaches ±1), so the wander fills its range.</summary>
        public double WanderGain { get; set; } = 1.6;

        /// <summary>Range of the wander's wavelength along the roll, metres, drawn once per prop.</summary>
        public double MinWanderPeriod { get; set; } = 20.0;
        public double MaxWanderPeriod { get; set; } = 60.0;

        /// <summary>Range of the gap between seeded course change spots, metres; each fires with <see cref="CourseChance"/>.</summary>
        public double MinCourseSpacing { get; set; } = 30.0;
        public double MaxCourseSpacing { get; set; } = 90.0;
        public double CourseChance { get; set; } = 0.35;

        /// <summary>Most a course change turns the roll's mean heading off the wind, degrees, eased in over <see cref="CourseBlend"/> metres.</summary>
        public double CourseDegrees { get; set; } = 20.0;
        public double CourseBlend { get; set; } = 10.0;

        /// <summary>Range of a prop's size over its kind's mesh; size = lerp(min, max, u^<see cref="SizeBias"/>).</summary>
        public double MinSize { get; set; } = 0.65;
        public double MaxSize { get; set; } = 1.45;

        /// <summary>Power on the size draw: above 1 makes small props common and big ones rare.</summary>
        public double SizeBias { get; set; } = 2.0;

        /// <summary>Range of the jump over an obstacle run of the support (a rail head, a low rock) at size 1, metres; it scales with the prop's size.</summary>
        public double MinObstacleJump { get; set; } = 0.2;
        public double MaxObstacleJump { get; set; } = 0.5;

        /// <summary>Range of the small pop over a crest run (an embankment's shoulder), metres.</summary>
        public double MinCrestJump { get; set; } = 0.08;
        public double MaxCrestJump { get; set; } = 0.2;

        /// <summary>Range of the gap between seeded bump spots on the way, metres; each fires with the path's bump chance.</summary>
        public double MinBumpSpacing { get; set; } = 12.0;
        public double MaxBumpSpacing { get; set; } = 40.0;

        /// <summary>Range of the chance a path's bump spots fire, drawn once per path.</summary>
        public double MinBumpChance { get; set; } = 0.3;
        public double MaxBumpChance { get; set; } = 0.6;

        /// <summary>Range of a bump's jump, metres.</summary>
        public double MinBumpJump { get; set; } = 0.1;
        public double MaxBumpJump { get; set; } = 0.4;

        /// <summary>Height of the one small bounce after a landing as a share of the jump's (0: none).</summary>
        public double BounceShare { get; set; } = 0.3;

        /// <summary>Wind speed the jumps' arcs are shaped for, metres per second: a jump of height h spans the flight time of h at this wind's rolling speed.</summary>
        public double JumpWindSpeed { get; set; } = 3.0;

        /// <summary>Metres before an obstacle or crest run a prop takes off, and least past it that it lands.</summary>
        public double JumpLead { get; set; } = 0.1;

        /// <summary>Steepest rise of a jump's arc over its length (rise per metre at the takeoff), so short hops stay readable.</summary>
        public double MaxJumpSlope { get; set; } = 2.5;

        /// <summary>Least clearance of a jump's arc over the prop's contact height at its middle, metres.</summary>
        public double JumpClearance { get; set; } = 0.03;

        /// <summary>
        /// Range of the share of the wind's drift along the prevailing wind a prop rolls: the lightest props roll near the
        /// maximum, the heaviest near the minimum, each jittered by <see cref="SpeedJitter"/> of the range.
        /// </summary>
        public double MinGroundSpeedShare { get; set; } = 0.4;
        public double MaxGroundSpeedShare { get; set; } = 0.85;
        public double SpeedJitter { get; set; } = 0.3;

        /// <summary>Most a prop's mesh is squashed along any of its axes (its widest axis keeps full size).</summary>
        public double MaxSquash { get; set; } = 0.08;

        /// <summary>Most a prop's mesh is tipped off its roll axis, degrees, so no two spin alike.</summary>
        public double MaxTiltDegrees { get; set; } = 10.0;

        /// <summary>
        /// Drift metres (wind travel along the prevailing direction) a prop scales in over after its spawn and out over
        /// before its end; the caller sets it to a second of the slowest wind so a fade never takes longer than that.
        /// </summary>
        public double FadeDrift { get; set; } = 1.4;

        /// <summary>Step the ground profile is sampled at, metres: a ball crossing a rail head sags under 1 cm between samples.</summary>
        public double ProfileStep { get; set; } = 0.05;

        /// <summary>Step the roll is tested against the caller's blockers at, metres.</summary>
        public double ClipStep { get; set; } = 0.25;

        /// <summary>The shipped rollers.</summary>
        public static RollerPathSettings Default => new RollerPathSettings();

        /// <summary>Most drift metres any prop lives: its longest roll at the slowest share.</summary>
        public double MostLifeDrift => MaxLife / MinGroundSpeedShare;

        /// <summary>Throws when a range is inverted or a length, step or share is not positive.</summary>
        public void Validate() {
            RequireRange(MinLife, MaxLife, nameof(MinLife));
            RequireRange(MinWanderPeriod, MaxWanderPeriod, nameof(MinWanderPeriod));
            RequireRange(MinCourseSpacing, MaxCourseSpacing, nameof(MinCourseSpacing));
            RequireRange(MinObstacleJump, MaxObstacleJump, nameof(MinObstacleJump));
            RequireRange(MinCrestJump, MaxCrestJump, nameof(MinCrestJump));
            RequireRange(MinBumpSpacing, MaxBumpSpacing, nameof(MinBumpSpacing));
            RequireRange(MinBumpChance, MaxBumpChance, nameof(MinBumpChance));
            RequireRange(MinBumpJump, MaxBumpJump, nameof(MinBumpJump));
            RequireRange(MinSize, MaxSize, nameof(MinSize));
            RequireRange(MinGroundSpeedShare, MaxGroundSpeedShare, nameof(MinGroundSpeedShare));
            RequirePositive(MinLife, nameof(MinLife));
            RequirePositive(MinSize, nameof(MinSize));
            RequirePositive(SizeBias, nameof(SizeBias));
            RequirePositive(MinWanderPeriod, nameof(MinWanderPeriod));
            RequirePositive(MinCourseSpacing, nameof(MinCourseSpacing));
            RequirePositive(MinBumpSpacing, nameof(MinBumpSpacing));
            RequirePositive(JumpWindSpeed, nameof(JumpWindSpeed));
            RequirePositive(MaxJumpSlope, nameof(MaxJumpSlope));
            RequirePositive(ProfileStep, nameof(ProfileStep));
            RequirePositive(ClipStep, nameof(ClipStep));
            RequirePositive(MinGroundSpeedShare, nameof(MinGroundSpeedShare));
            RequirePositive(FadeDrift, nameof(FadeDrift));
            RequirePositive(CourseBlend, nameof(CourseBlend));
            if (MinClippedLength < 0.0) throw new ArgumentException($"{nameof(MinClippedLength)} must not be negative.");
            if (MinObstacleJump < 0.0 || MinCrestJump < 0.0 || MinBumpJump < 0.0 || JumpLead < 0.0 || JumpClearance < 0.0) throw new ArgumentException("Jump heights, lead and clearance must not be negative.");
            if (!InUnit(SpeedJitter) || !InUnit(CourseChance)) throw new ArgumentException("Speed jitter and course chance must be in [0, 1].");
            if (!InUnit(MinBumpChance) || !InUnit(MaxBumpChance) || !InUnit(BounceShare)) throw new ArgumentException("Bump chances and the bounce share must be in [0, 1].");
            if (MaxSquash < 0.0 || MaxSquash >= 1.0 || MaxTiltDegrees < 0.0 || MaxTiltDegrees > 90.0) throw new ArgumentException("Squash must be in [0, 1) and tilt in [0, 90] degrees.");
            if (WanderDegrees < 0.0 || WanderDegrees > MaxOffWindDegrees || WanderGain < 0.0) throw new ArgumentException($"{nameof(WanderDegrees)} must be in [0, {MaxOffWindDegrees}] and the gain not negative.");
            if (CourseDegrees < 0.0 || CourseDegrees > MaxOffWindDegrees) throw new ArgumentException($"{nameof(CourseDegrees)} must be in [0, {MaxOffWindDegrees}].");
        }

        private static bool InUnit(double value) {
            return value >= 0.0 && value <= 1.0;
        }

        private static void RequireRange(double min, double max, string name) {
            if (!(min <= max)) throw new ArgumentException($"{name} must not exceed its maximum ({min} > {max}).");
        }

        private static void RequirePositive(double value, string name) {
            if (!(value > 0.0)) throw new ArgumentException($"{name} must be positive ({value}).");
        }
    }
}
