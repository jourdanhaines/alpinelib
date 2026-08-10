using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// One rule in the server-side chat pipeline. Filters run in order, and the first refusal ends the
    /// chain — so the cheap checks are configured first and the expensive ones last.
    /// </summary>
    /// <remarks>
    /// Synchronous by contract. The pipeline runs on the game-loop thread, and a filter that wanted to
    /// await something would either block that thread or reorder chat behind the loop's back; anything
    /// genuinely asynchronous belongs in an external provider, not here.
    /// <para>
    /// Filters may hold per-player state, which is why <see cref="Forget"/> exists: a player who leaves
    /// for good must not leave a dictionary entry behind on a server that runs for weeks.
    /// </para>
    /// </remarks>
    public interface IChatFilter {
        /// <summary>Names the rule for logs and moderation records.</summary>
        string Name { get; }

        /// <summary>Rules on one send attempt.</summary>
        ChatFilterResult Evaluate(in ChatFilterContext context);

        /// <summary>Drops any state held for a player who has left. Must tolerate unknown players.</summary>
        void Forget(PlayerId player);
    }
}
