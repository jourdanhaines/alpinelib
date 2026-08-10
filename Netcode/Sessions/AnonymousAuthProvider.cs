using System.Threading;
using System.Threading.Tasks;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The v1 client auth provider: hands over empty credentials.
    /// </summary>
    /// <remarks>
    /// Anonymous identity is minted and persisted client-side, so there is nothing to acquire. It
    /// exists as a real implementation rather than a null check so the auth path is exercised end to
    /// end from day one and a Steam provider is a swap, not a new code path.
    /// </remarks>
    public sealed class AnonymousAuthProvider : IAuthProvider {
        /// <inheritdoc />
        public AuthMethod Method => AuthMethod.Anonymous;

        /// <inheritdoc />
        public Task<AuthCredentials> AcquireAsync(PlayerIdentity identity, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(AuthCredentials.Anonymous());
        }
    }
}
