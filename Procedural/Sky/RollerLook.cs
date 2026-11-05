namespace AlpineLib.Procedural.Sky {
    /// <summary>How one spawned wind-rolled prop is drawn: its kind (mesh), ball radius at size 1 and outline (null: a ball).</summary>
    public readonly struct RollerLook {
        public RollerLook(int kind, double radius, RollerShape shape) {
            Kind = kind;
            Radius = radius;
            Shape = shape;
        }

        public int Kind { get; }

        public double Radius { get; }

        public RollerShape Shape { get; }
    }
}
