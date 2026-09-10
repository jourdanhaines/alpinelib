namespace AlpineLib.Networking {
    /// <summary>
    /// Implemented by whatever on a pawn knows which carrier it is currently riding, so the replication
    /// side can ask without knowing how the game decides.
    /// </summary>
    /// <remarks>
    /// The decision is genuinely a game's own: one project reads a ground probe, another a trigger
    /// volume, another a parenting rule. What the library needs is only the answer, once per send, and a
    /// one-property interface keeps <see cref="NetActorSync"/> from growing a dependency on any of those
    /// mechanisms. Null means world space, which is both the default and the answer for the great
    /// majority of pawns.
    /// </remarks>
    public interface INetCarrierSource {
        /// <summary>The carrier the pawn is riding, or null when it stands in the world.</summary>
        NetCarrier CurrentCarrier { get; }
    }
}
