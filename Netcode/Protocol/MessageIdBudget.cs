using System;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// The whole-protocol view of the message id map: which ids the library has spoken for, and which
    /// band a game is free to author its own messages in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every band owns its own id constants — <c>CoreMessageIds</c>, <c>SessionMessageIds</c>,
    /// <c>ClaimMessageIds</c>, <c>ReplicationMessageIds</c>, <c>ChatMessageIds</c> — and none of them
    /// can see the others, least of all chat, which lives in a different assembly entirely. Nothing in
    /// the library could therefore answer "is this id already taken" until this type existed, and a game
    /// picking a number for its own message had only a comment in a plan document to go on. Handing a
    /// game an id the library already speaks is the worst collision this protocol has: the router
    /// refuses the second registration on the host that owns both, but a peer that only registers one of
    /// them decodes the wrong payload into the wrong handler and corrupts itself silently.
    /// </para>
    /// <para>
    /// The bands are written out as explicit ranges rather than derived from the id constants. The
    /// numbers here are a permanent wire contract that outlives any individual message: an id that a
    /// shipped build once spoke stays reserved after its message is retired and its constant deleted,
    /// because a client one build behind still speaks it. Reserving whole bands rather than the ids
    /// currently in use is what keeps that true without anyone having to remember it.
    /// </para>
    /// </remarks>
    public static class MessageIdBudget {
        /// <summary>First id of the connection band — the messages the facades themselves speak.</summary>
        public const ushort CoreBandStart = 0;

        /// <summary>Last reserved id of the connection band. Ids 3-63 of that band are free.</summary>
        public const ushort CoreBandEnd = 2;

        /// <summary>First id of the session band: handshake, roster, phase and front-desk traffic.</summary>
        public const ushort SessionBandStart = 64;

        /// <summary>Last id of the session band proper.</summary>
        public const ushort SessionBandEnd = 83;

        /// <summary>First id of the claim band: server-arbitrated exclusive slots.</summary>
        public const ushort ClaimBandStart = 84;

        /// <summary>Last id of the claim band.</summary>
        public const ushort ClaimBandEnd = 86;

        /// <summary>
        /// First id of the session tail, which carries ownership transfer and the ids held back for
        /// listen-host process migration.
        /// </summary>
        public const ushort SessionTailBandStart = 120;

        /// <summary>Last id of the session tail.</summary>
        public const ushort SessionTailBandEnd = 127;

        /// <summary>First id of the replication band: spawns, snapshots, inputs and corrections.</summary>
        public const ushort ReplicationBandStart = 128;

        /// <summary>Last id of the replication band.</summary>
        public const ushort ReplicationBandEnd = 135;

        /// <summary>First id a game may use for its own messages.</summary>
        public const ushort GameBandStart = 136;

        /// <summary>Last id a game may use for its own messages.</summary>
        public const ushort GameBandEnd = 191;

        /// <summary>The single envelope every chat frame rides in.</summary>
        public const ushort ChatEnvelopeId = 192;

        /// <summary>
        /// True when the library has spoken for this id and a game must not use it. Covers ids that are
        /// held in reserve as well as ids in use today; see the note on the type for why.
        /// </summary>
        public static bool IsReservedByLibrary(ushort id) {
            return IsInRange(id, CoreBandStart, CoreBandEnd)
                || IsInRange(id, SessionBandStart, SessionBandEnd)
                || IsInRange(id, ClaimBandStart, ClaimBandEnd)
                || IsInRange(id, SessionTailBandStart, SessionTailBandEnd)
                || IsInRange(id, ReplicationBandStart, ReplicationBandEnd)
                || id == ChatEnvelopeId;
        }

        /// <summary>
        /// True for the band a game should author its own message ids in. Ids outside it may still be
        /// unreserved — 3-63, 87-119 and everything past 192 are free too — but they sit between library
        /// bands that may grow, so a game that stays here never has to renumber.
        /// </summary>
        /// <remarks>
        /// Advice, not a gate. Nothing in the library refuses an id merely for sitting outside this
        /// band, because the free gaps are legitimately usable; what is refused is an id the library
        /// speaks, which is <see cref="IsReservedByLibrary"/>'s job.
        /// </remarks>
        public static bool IsInGameBand(ushort id) {
            return IsInRange(id, GameBandStart, GameBandEnd);
        }

        /// <summary>
        /// Throws unless a game may author on this id. Both halves of a game-owned channel call it, so
        /// a sender and a receiver cannot disagree about which ids are theirs to take.
        /// </summary>
        /// <param name="id">The id the caller wants to publish or listen on.</param>
        /// <param name="parameterName">The caller's own parameter name, for the thrown exception.</param>
        /// <exception cref="ArgumentException">The id is one the library already speaks.</exception>
        public static void GuardGameMessageId(ushort id, string parameterName) {
            if (!IsReservedByLibrary(id)) {
                return;
            }

            throw new ArgumentException(
                "Message id " + id.ToString() + " is reserved by the library. Author game messages in the "
                + GameBandStart.ToString() + "-" + GameBandEnd.ToString() + " band.",
                parameterName);
        }

        private static bool IsInRange(ushort id, ushort first, ushort last) {
            return id >= first && id <= last;
        }
    }
}
