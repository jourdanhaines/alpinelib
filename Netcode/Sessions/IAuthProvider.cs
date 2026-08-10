using System.Threading;
using System.Threading.Tasks;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Client half of the auth seam: produces the credentials sent in <c>AuthRequest</c>.
    /// </summary>
    /// <remarks>
    /// Async because a real provider talks to a platform SDK — acquiring a Steam session ticket is a
    /// round trip. The session client awaits this once, before it sends the request, and never on the
    /// tick path.
    /// </remarks>
    public interface IAuthProvider {
        /// <summary>Which method this provider produces credentials for.</summary>
        AuthMethod Method { get; }

        /// <summary>Acquires credentials for the local identity.</summary>
        Task<AuthCredentials> AcquireAsync(PlayerIdentity identity, CancellationToken cancellationToken);
    }
}
