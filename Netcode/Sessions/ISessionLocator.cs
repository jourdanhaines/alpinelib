using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Resolves a user-facing join target to the endpoint of the game server to dial.
    /// </summary>
    /// <remarks>
    /// A locator resolves the SERVER, never a session. Join codes select a session after the
    /// connection exists, so they never travel through here — that separation is what lets one
    /// dedicated server host many igloos behind a single address. <c>ConfiguredServerLocator</c> is
    /// the v1 implementation; a backend-directory locator is the seam for multi-server deployments.
    /// </remarks>
    public interface ISessionLocator {
        /// <summary>True when this locator can turn the given target into an endpoint.</summary>
        bool CanResolve(string target);

        /// <summary>Resolves the target, or throws when it cannot be resolved.</summary>
        Task<NetEndpoint> ResolveAsync(string target, CancellationToken cancellationToken);
    }
}
