namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The one thing <see cref="PawnMotor"/> needs from the world it is stepping through: how high the
    /// floor is under a point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The motor is shared code and cannot see a collision world, so the ground is a seam instead of a
    /// dependency. A dedicated server runs <see cref="FlatGroundProvider"/> in v1 and a baked heightfield
    /// later; a Unity listen host supplies a raycasting implementation so the host's simulation matches
    /// what a player actually walks on.
    /// </para>
    /// <para>
    /// Implementations must be <b>pure and deterministic</b>: the same coordinates must return the same
    /// float on every call, on both ends of the wire, or prediction and authority will disagree forever.
    /// That rules out anything that samples mutable scene state mid-tick.
    /// </para>
    /// </remarks>
    public interface IGroundProvider {
        /// <summary>Height of the walkable surface under a horizontal position, in world units.</summary>
        float SampleHeight(float x, float z);
    }
}
