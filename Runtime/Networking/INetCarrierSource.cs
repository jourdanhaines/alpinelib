namespace AlpineLib.Networking {
    /// <summary>
    /// Implemented by whatever on a pawn knows which carrier it is currently riding, so the replication
    /// side can ask without knowing how the game decides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decision is genuinely a game's own: one project reads a ground probe, another a trigger
    /// volume, another a parenting rule. What the library needs is only the answer, once per send, and a
    /// one-property interface keeps <see cref="NetActorSync"/> from growing a dependency on any of those
    /// mechanisms. Null means world space, which is both the default and the answer for the great
    /// majority of pawns.
    /// </para>
    /// <para>
    /// <b>Implementations must apply hysteresis.</b> A change of carrier is only to be reported once the
    /// new answer — a different carrier, or none — has been the answer continuously for at least
    /// <see cref="NetCarrier.SourceHysteresisSeconds"/>; until then this property keeps handing back the
    /// carrier it last reported. That is a contract, not advice, because the raw signal underneath is
    /// nearly always noisy: a ground probe standing across the gap between two coupled cars picks
    /// whichever collider the physics engine reported last and can alternate every single frame, and a
    /// hop or a step over a rail breaks contact for a handful of frames without the rider ever leaving
    /// the deck. Each of those alternations is a frame change on the wire, every one of which the server
    /// must accept unmeasured — see <c>MovementValidator</c>'s trust-boundary note — so a source with no
    /// dwell timer spends a budget that exists to bound a cheat, and spends it while the player is doing
    /// nothing but walking. Settling first costs a rider at most a third of a second of replicating in
    /// the frame it is leaving, which is a fraction of a metre of deck.
    /// </para>
    /// </remarks>
    public interface INetCarrierSource {
        /// <summary>
        /// The carrier the pawn is riding, or null when it stands in the world. Changes only after the
        /// new answer has held for <see cref="NetCarrier.SourceHysteresisSeconds"/>; see the type note.
        /// </summary>
        NetCarrier CurrentCarrier { get; }
    }
}
