using System.Threading;
using System.Threading.Tasks;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The v1 server validator: trusts the claimed identity, but still normalises it.
    /// </summary>
    /// <remarks>
    /// Anonymous play means the server cannot verify who anyone is, so this rejects only what would
    /// break the roster: a missing player id (rejoin reservations are keyed on it) or an unusable
    /// display name. Names are substituted rather than refused, because a blank name should not cost
    /// a player their connection.
    /// </remarks>
    public sealed class AnonymousAuthValidator : IAuthValidator {
        private readonly string _defaultDisplayName;

        /// <summary>Creates a validator that falls back to "Penguin" for blank names.</summary>
        public AnonymousAuthValidator() : this("Penguin") { }

        /// <summary>Creates a validator with a specific fallback display name.</summary>
        public AnonymousAuthValidator(string defaultDisplayName) {
            _defaultDisplayName = string.IsNullOrWhiteSpace(defaultDisplayName)
                ? "Penguin"
                : defaultDisplayName.Trim();
        }

        /// <inheritdoc />
        public AuthMethod Method => AuthMethod.Anonymous;

        /// <inheritdoc />
        public Task<AuthVerdict> ValidateAsync(
            PlayerIdentity claimedIdentity,
            AuthCredentials credentials,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Validate(claimedIdentity, credentials));
        }

        private AuthVerdict Validate(PlayerIdentity claimedIdentity, AuthCredentials credentials) {
            if (claimedIdentity == null || !claimedIdentity.PlayerId.IsValid) {
                return AuthVerdict.Reject("Missing player id.");
            }

            if (credentials != null && credentials.Method != AuthMethod.Anonymous) {
                return AuthVerdict.Reject("Server accepts anonymous authentication only.");
            }

            string displayName = PlayerIdentity.Sanitize(claimedIdentity.DisplayName);

            if (displayName.Length == 0) {
                displayName = _defaultDisplayName;
            }

            PlayerIdentity resolved = new PlayerIdentity(
                claimedIdentity.PlayerId,
                displayName,
                AuthMethod.Anonymous);

            return AuthVerdict.Accept(resolved);
        }
    }
}
