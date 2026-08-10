using System.Threading;
using System.Threading.Tasks;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Server half of the auth seam: decides whether a claimed identity is real.
    /// </summary>
    /// <remarks>
    /// The one approval hook in the stack — the transport layer only gates protocol version. Async
    /// because a Steam validator calls the Steamworks Web API; the session host never awaits this
    /// inline, it posts the completion to the tick inbox so the game loop stays single-threaded.
    /// </remarks>
    public interface IAuthValidator {
        /// <summary>Which method this validator understands.</summary>
        AuthMethod Method { get; }

        /// <summary>Validates the credentials against the identity the client claimed.</summary>
        Task<AuthVerdict> ValidateAsync(
            PlayerIdentity claimedIdentity,
            AuthCredentials credentials,
            CancellationToken cancellationToken);
    }
}
