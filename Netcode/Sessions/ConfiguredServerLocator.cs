using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The v1 locator: every client dials the one server address from the matchmaking config,
    /// whatever the join target says.
    /// </summary>
    /// <remarks>
    /// Deliberately ignores the target. Hosting an igloo and joining one both go to the same server —
    /// what differs is the request sent afterwards (create versus join by code). Keeping that
    /// asymmetry out of the locator is why join codes never encode endpoints.
    /// </remarks>
    public sealed class ConfiguredServerLocator : ISessionLocator {
        private readonly NetEndpoint _endpoint;

        /// <summary>Creates a locator for a "host:port" address, as authored on the matchmaking config.</summary>
        public ConfiguredServerLocator(string serverAddress) {
            if (!TryParseAddress(serverAddress, out NetEndpoint parsed)) {
                throw new ArgumentException(
                    "Server address '" + (serverAddress ?? "<null>") + "' is not in host:port form.",
                    nameof(serverAddress));
            }

            _endpoint = parsed;
        }

        /// <summary>Creates a locator for an already-resolved endpoint.</summary>
        public ConfiguredServerLocator(NetEndpoint endpoint) {
            _endpoint = endpoint;
        }

        /// <summary>The endpoint every resolve returns.</summary>
        public NetEndpoint Endpoint => _endpoint;

        /// <inheritdoc />
        public bool CanResolve(string target) {
            return _endpoint.IsValid;
        }

        /// <inheritdoc />
        public Task<NetEndpoint> ResolveAsync(string target, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_endpoint.IsValid) {
                throw new InvalidOperationException(
                    "ConfiguredServerLocator has no valid server address configured.");
            }

            return Task.FromResult(_endpoint);
        }

        /// <summary>
        /// Parses "host:port", including the bracketed "[::1]:9050" form for IPv6 literals.
        /// </summary>
        public static bool TryParseAddress(string serverAddress, out NetEndpoint endpoint) {
            endpoint = NetEndpoint.None;

            if (string.IsNullOrWhiteSpace(serverAddress)) {
                return false;
            }

            string trimmed = serverAddress.Trim();
            int separatorIndex = FindPortSeparator(trimmed);

            if (separatorIndex <= 0 || separatorIndex == trimmed.Length - 1) {
                return false;
            }

            string host = UnwrapBrackets(trimmed.Substring(0, separatorIndex));
            string portText = trimmed.Substring(separatorIndex + 1);

            if (host.Length == 0) {
                return false;
            }

            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port)) {
                return false;
            }

            if (port <= 0 || port > 65535) {
                return false;
            }

            endpoint = NetEndpoint.Direct(host, port);
            return true;
        }

        /// <summary>
        /// Finds the colon that separates host from port, ignoring the colons inside an IPv6 literal.
        /// </summary>
        private static int FindPortSeparator(string address) {
            int closingBracketIndex = address.LastIndexOf(']');

            if (closingBracketIndex >= 0) {
                int bracketedSeparator = address.IndexOf(':', closingBracketIndex);
                return bracketedSeparator;
            }

            // A bare IPv6 literal has several colons and no port; only the single-colon form is an address.
            if (address.IndexOf(':') != address.LastIndexOf(':')) {
                return -1;
            }

            return address.LastIndexOf(':');
        }

        private static string UnwrapBrackets(string host) {
            if (host.Length >= 2 && host[0] == '[' && host[host.Length - 1] == ']') {
                return host.Substring(1, host.Length - 2);
            }

            return host;
        }
    }
}
