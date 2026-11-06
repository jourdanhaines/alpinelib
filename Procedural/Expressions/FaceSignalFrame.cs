namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// What a body did since the face last stepped, as plain numbers a game fills from its own state: off the
    /// ground and for how long, the jump and landing edges, how hard it is moving, a downward glance, a gaze, and the
    /// turn, head pitch and move direction the eyes follow.
    /// </summary>
    /// <remarks>
    /// The default value is a body standing still on the ground and looking ahead. Turn and move are the body's
    /// own, relative to whatever carries it: a rider standing on a train going round a curve neither turns nor moves.
    /// </remarks>
    public struct FaceSignalFrame {
        /// <summary>True while the body is off the ground (a jump counts from take-off).</summary>
        public bool Airborne { get; set; }

        /// <summary>Seconds of real air so far, zero on the ground.</summary>
        public float AirTime { get; set; }

        /// <summary>True on the step a jump started.</summary>
        public bool Jumped { get; set; }

        /// <summary>True on the step the body touched down.</summary>
        public bool Landed { get; set; }

        /// <summary>Air time of the flight that just ended; read when <see cref="Landed"/>.</summary>
        public float LandAirTime { get; set; }

        /// <summary>How hard the body is moving, 0 (standing, walking) to 1 (flat out).</summary>
        public float Exertion { get; set; }

        /// <summary>How far the eyes are drawn down toward something held, 0 to 1.</summary>
        public float GlanceDown { get; set; }

        /// <summary>Gaze across, -1 full left to 1 full right.</summary>
        public float LookRight { get; set; }

        /// <summary>Gaze up and down, -1 full down to 1 full up.</summary>
        public float LookUp { get; set; }

        /// <summary>How fast the body is turning, degrees per second, positive to its right (see <see cref="FaceTurnTracker"/>).</summary>
        public float TurnRate { get; set; }

        /// <summary>Head pitch in degrees, positive looking down, 0 level.</summary>
        public float LookPitch { get; set; }

        /// <summary>The body's move to its own right, in multiples of its base move speed (negative is left).</summary>
        public float MoveRight { get; set; }

        /// <summary>The body's move along its own facing, in multiples of its base move speed (negative is backwards).</summary>
        public float MoveForward { get; set; }
    }
}
