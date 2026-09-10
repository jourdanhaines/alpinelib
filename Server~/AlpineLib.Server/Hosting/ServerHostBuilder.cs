using System;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlpineLib.Server.Hosting {
    /// <summary>
    /// What a game says about itself before <see cref="AlpineServerHost"/> stands the process up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything on it is optional. A server built with an untouched builder hosts sessions, replicates
    /// pawns, arbitrates slots and carries chat — which is a complete multiplayer server with no game in
    /// it. A game adds its simulation through <see cref="ModuleFactory"/>, and that is normally the only
    /// line it writes.
    /// </para>
    /// <para>
    /// <see cref="Services"/> is the escape hatch for a game whose module needs something injected: a
    /// registration here is resolved by the same container the library's own singletons come out of, so
    /// a module factory can take a dependency in its constructor rather than reaching for a static.
    /// </para>
    /// </remarks>
    public sealed class ServerHostBuilder {
        /// <summary>Builds the game's per-session simulation. Null hosts sessions and nothing more.</summary>
        public ISessionModuleFactory ModuleFactory { get; set; }

        /// <summary>
        /// Where a session puts its arrivals, given the loaded configuration. Null reads the exported
        /// <c>spawn</c> section, which is what a game that authors its spawn points in Unity wants.
        /// </summary>
        /// <remarks>
        /// Called once per session opened, not once per process: a placement carries the seat counter of
        /// the session it belongs to, so every call must hand back a fresh instance.
        /// </remarks>
        public Func<ServerConfigBundle, ISpawnPlacement> PlacementFactory { get; set; }

        /// <summary>Extra services the game's module factory resolves out of.</summary>
        public IServiceCollection Services { get; } = new ServiceCollection();

        /// <summary>
        /// Applied to the host's logging after the defaults, so a game can quieten a category or add a
        /// provider. Null leaves the console logging the generic host sets up.
        /// </summary>
        public Action<ILoggingBuilder> ConfigureLogging { get; set; }

        /// <summary>
        /// Applied to the host options after the configuration file and the command line, which makes it
        /// the last word — use it for a default a game wants that no deployment overrode.
        /// </summary>
        public Action<ServerRuntimeOptions> ConfigureOptions { get; set; }
    }
}
