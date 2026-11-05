namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One streamed cell's part of an <see cref="IRollerWorld"/>, in the cell's local plane (origin at the cell's centre).
    /// <see cref="RollerCellWorld"/> routes each point to the cell it lies in. Must be safe to read from a worker.
    /// </summary>
    public interface IRollerCell {
        RollerBlock BlockAt(double x, double z, double radius);

        RollerSupport SupportAt(double x, double z, double radius);

        double SurfaceAt(double x, double z);
    }
}
