namespace AlpineLib.Netcode.Diagnostics {
    /// <summary>
    /// How often a <see cref="MonoParityGoldens"/> motor run reached the motor paths a golden could otherwise miss,
    /// so a test can prove the hash covers them.
    /// </summary>
    public sealed class MotorParityCoverage {
        /// <summary>Steps taken airborne with no input under air drag (the <c>MathF.Exp</c> decay path).</summary>
        public int AirborneWithoutInput { get; internal set; }

        /// <summary>Inputs longer than one, which the motor clamps back to unit length.</summary>
        public int OverLengthInputs { get; internal set; }

        /// <summary>Steps with no input whose yaw was held.</summary>
        public int HeldYaws { get; internal set; }

        /// <summary>Steps ending against or on a prism.</summary>
        public int PrismTouches { get; internal set; }

        /// <summary>Steps ending on the 45° ramp's slope.</summary>
        public int RampTouches { get; internal set; }

        /// <summary>World-frame pawn states moved by the floating-origin rebase.</summary>
        public int OriginShifts { get; internal set; }

        /// <summary>Times the looping mover jumped back to its first waypoint.</summary>
        public int LoopWraps { get; internal set; }

        /// <summary>Sloped-world steps ending grounded on a sloped prism.</summary>
        public int SlopedPrismGrounded { get; internal set; }

        /// <summary>Sloped-world steps ending against or on a sloped prism.</summary>
        public int SlopedPrismTouches { get; internal set; }
    }
}
