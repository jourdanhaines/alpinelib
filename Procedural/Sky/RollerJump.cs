namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One leap of a wind-rolled prop between two rolled distances of its path: a parabola of <see cref="Height"/> over
    /// the straight line from the takeoff's contact height to the landing's, the prop turning on as it flies.
    /// </summary>
    public readonly struct RollerJump {
        public RollerJump(RollerJumpKind kind, double takeoff, double landing, double height, double takeoffY, double landingY) {
            Kind = kind;
            Takeoff = takeoff;
            Landing = landing;
            Height = height;
            TakeoffY = takeoffY;
            LandingY = landingY;
        }

        public RollerJumpKind Kind { get; }

        /// <summary>Rolled metres where the prop leaves and meets the ground.</summary>
        public double Takeoff { get; }
        public double Landing { get; }

        /// <summary>Rise of the arc over the line between the two contacts at its middle, metres.</summary>
        public double Height { get; }

        /// <summary>Centre heights at the takeoff and the landing (in contact there).</summary>
        public double TakeoffY { get; }
        public double LandingY { get; }

        public double Length => Landing - Takeoff;

        /// <summary>True when <paramref name="distance"/> is strictly between the takeoff and the landing.</summary>
        public bool Contains(double distance) {
            return distance > Takeoff && distance < Landing;
        }

        /// <summary>Centre height at <paramref name="distance"/> inside the jump.</summary>
        public double HeightAt(double distance) {
            double share = (distance - Takeoff) / (Landing - Takeoff);
            return TakeoffY + (LandingY - TakeoffY) * share + 4.0 * Height * share * (1.0 - share);
        }
    }
}
