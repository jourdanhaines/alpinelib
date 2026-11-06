namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// What a body did since the face last stepped, as plain numbers a game fills from its own state: off the
    /// ground and for how long, the jump and landing edges, how hard it is moving, a downward glance and a gaze.
    /// </summary>
    /// <remarks>The default value is a body standing still on the ground and looking ahead.</remarks>
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
    }
}
