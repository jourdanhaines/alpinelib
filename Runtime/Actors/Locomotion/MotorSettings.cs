namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// The tunables one motor step reads: the capsule, the ground rules and the air model.
    /// </summary>
    public struct MotorSettings {
        public CapsuleShape Capsule;
        public float Gravity;
        public float JumpSpeed;
        public float AirAcceleration;
        public float AirDrag;
        public float StepOffset;
        public float SlopeLimitDegrees;
        public float SkinWidth;
        public float LandingProbeDistance;
    }
}
