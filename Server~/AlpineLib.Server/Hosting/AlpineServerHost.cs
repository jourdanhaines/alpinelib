using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.GameLoop;
using AlpineLib.Server.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlpineLib.Server.Hosting {
    /// <summary>
    /// The whole dedicated server in one call: read the command line, load the exported configuration,
    /// bind the socket, and step until something stops it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A game's <c>Program.Main</c> is meant to be two lines — this call and whatever it passes on the
    /// builder. Everything that is the same for every game lives here: the option sources and their
    /// precedence, the transport, the front desk, the fixed-step loop, and the order they are torn down
    /// in.
    /// </para>
    /// <para>
    /// <b>Plain .NET, no web framework.</b> There is no Kestrel, no gRPC and no API key anywhere in this
    /// assembly, so the process runs on a machine that has only the base runtime and a test can drive a
    /// real host — sockets, loop thread and all — without one either. A deployment that wants an
    /// operator surface builds its own host around these services rather than having one forced on it.
    /// </para>
    /// <para>
    /// <b>Configuration is fatal, geometry is not.</b> The rules a client was shipped with cannot be
    /// guessed, so a missing export stops the process with a message naming the path. A scene's shapes
    /// can be guessed — flat ground at y = 0 — so a missing geometry directory is a warning loud enough
    /// to find in a log and no worse.
    /// </para>
    /// </remarks>
    public static class AlpineServerHost {
        /// <summary>Log category the geometry load reports under, since the library itself carries no logger.</summary>
        public const string GeometryLogCategory = "AlpineLib.Server.Geometry";

        /// <summary>Builds the host and runs it until it is stopped or the idle window runs out.</summary>
        /// <exception cref="ArgumentException">The command line carries something unrecognised.</exception>
        /// <exception cref="InvalidOperationException">The exported configuration is missing or malformed.</exception>
        public static Task RunAsync(string[] args, Action<ServerHostBuilder> configure) {
            return RunAsync(args, configure, CancellationToken.None);
        }

        /// <summary>Builds the host and runs it, stopping early when the token is cancelled.</summary>
        public static async Task RunAsync(string[] args, Action<ServerHostBuilder> configure, CancellationToken cancellationToken) {
            using IHost host = Build(args, configure);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds the host without running it, for a caller that wants to start and stop it itself.
        /// </summary>
        /// <remarks>
        /// The same wiring <see cref="RunAsync(string[], Action{ServerHostBuilder})"/> uses, exposed
        /// because a test that asserts on startup ordering has to hold the host rather than await it.
        /// </remarks>
        public static IHost Build(string[] args, Action<ServerHostBuilder> configure) {
            ServerHostBuilder gameBuilder = new ServerHostBuilder();
            configure?.Invoke(gameBuilder);

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            ServerRuntimeOptions options = ReadOptions(builder, args, gameBuilder);
            ServerConfigBundle config = LoadSessionConfig(builder, options);

            gameBuilder.ConfigureLogging?.Invoke(builder.Logging);
            RegisterGameServices(builder, gameBuilder, options, config);
            CopyGameServices(builder, gameBuilder);

            return builder.Build();
        }

        /// <summary>
        /// Resolves the options every source has had its say on: the file first, then the command line,
        /// then whatever the game insists on.
        /// </summary>
        /// <remarks>
        /// The generic host is deliberately built with no arguments of its own. Its command-line provider
        /// would read <c>--port</c> as a configuration key and silently accept a flag this server does not
        /// understand, which is exactly what <see cref="ServerArguments"/> exists to refuse.
        /// </remarks>
        private static ServerRuntimeOptions ReadOptions(HostApplicationBuilder builder, string[] args, ServerHostBuilder gameBuilder) {
            ServerRuntimeOptions options =
                builder.Configuration.GetSection(ServerRuntimeOptions.SectionName).Get<ServerRuntimeOptions>()
                ?? new ServerRuntimeOptions();

            options.ApplyArguments(ServerArguments.Parse(args));
            gameBuilder.ConfigureOptions?.Invoke(options);
            return options;
        }

        private static ServerConfigBundle LoadSessionConfig(HostApplicationBuilder builder, ServerRuntimeOptions options) {
            // An unset path stays unset rather than resolving to the content root itself: the loader has a
            // far better message for "nobody configured one" than File.Exists has for a directory.
            string path = options.ResolveSessionConfigPath(builder.Environment.ContentRootPath);
            return SessionConfigLoader.LoadFromFile(path);
        }

        private static void RegisterGameServices(
            HostApplicationBuilder builder,
            ServerHostBuilder gameBuilder,
            ServerRuntimeOptions options,
            ServerConfigBundle config) {
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton(config.Net);
            builder.Services.AddSingleton(gameBuilder);
            builder.Services.AddSingleton<GameThreadInbox>();
            builder.Services.AddSingleton<GameLoopHeartbeat>();
            builder.Services.AddSingleton<IAuthValidator>(CreateAuthValidator);
            builder.Services.AddSingleton<INetTransport>(CreateTransport);
            builder.Services.AddSingleton(CreateNetServer);
            builder.Services.AddSingleton(CreateGeometryLibrary);
            builder.Services.AddSingleton(CreateSessionRegistry);
            builder.Services.AddHostedService<GameLoopService>();
        }

        /// <summary>Folds the game's own registrations into the host's container.</summary>
        private static void CopyGameServices(HostApplicationBuilder builder, ServerHostBuilder gameBuilder) {
            foreach (ServiceDescriptor descriptor in gameBuilder.Services) {
                builder.Services.Add(descriptor);
            }
        }

        private static IAuthValidator CreateAuthValidator(IServiceProvider services) {
            ServerConfigBundle config = services.GetRequiredService<ServerConfigBundle>();
            return new AnonymousAuthValidator(config.Session.DefaultDisplayName);
        }

        private static INetTransport CreateTransport(IServiceProvider services) {
            ServerConfigBundle config = services.GetRequiredService<ServerConfigBundle>();
            return new LiteNetTransport(config.Net.DisconnectTimeoutMs);
        }

        /// <summary>
        /// The one socket this process binds, on the port the command line asked for or the export named.
        /// </summary>
        /// <remarks>
        /// The override is written onto the shared <c>NetConfig</c> rather than passed alongside it,
        /// because the config is what <c>NetServer.Start</c> reads the port from and a second source of
        /// truth here would be a port two pieces of code could disagree about.
        /// </remarks>
        private static NetServer CreateNetServer(IServiceProvider services) {
            INetTransport transport = services.GetRequiredService<INetTransport>();
            ServerConfigBundle config = services.GetRequiredService<ServerConfigBundle>();
            ServerRuntimeOptions options = services.GetRequiredService<ServerRuntimeOptions>();

            if (options.PortOverride.HasValue) {
                config.Net.Port = options.PortOverride.Value;
            }

            return new NetServer(transport, config.Net);
        }

        private static SessionRegistry CreateSessionRegistry(IServiceProvider services) {
            ServerRuntimeOptions options = services.GetRequiredService<ServerRuntimeOptions>();
            ServerHostBuilder gameBuilder = services.GetRequiredService<ServerHostBuilder>();

            return new SessionRegistry(
                services.GetRequiredService<NetServer>(),
                services.GetRequiredService<ServerConfigBundle>(),
                services.GetRequiredService<IAuthValidator>(),
                services.GetRequiredService<SceneGeometryLibrary>(),
                gameBuilder.ModuleFactory,
                gameBuilder.PlacementFactory,
                options.MaxSessions,
                ReadWallClockUnixMs,
                services.GetRequiredService<ILogger<SessionRegistry>>());
        }

        private static SceneGeometryLibrary CreateGeometryLibrary(IServiceProvider services) {
            ServerRuntimeOptions options = services.GetRequiredService<ServerRuntimeOptions>();
            ServerConfigBundle config = services.GetRequiredService<ServerConfigBundle>();
            IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(GeometryLogCategory);

            return LoadGeometryLibrary(options, config, environment.ContentRootPath, logger);
        }

        /// <summary>
        /// Reads every exported scene once, at startup, so a phase change later is a dictionary lookup
        /// rather than file I/O on the tick thread.
        /// </summary>
        /// <remarks>
        /// The directory is probed here rather than left to the library so that its absence can be said
        /// out loud. <see cref="SceneGeometryLibrary.LoadFromDirectory"/> answers a missing directory with
        /// an empty library on purpose — that is the right behaviour and the wrong silence for an operator
        /// who deployed the binary without the geometry beside it.
        /// </remarks>
        public static SceneGeometryLibrary LoadGeometryLibrary(
            ServerRuntimeOptions options,
            ServerConfigBundle config,
            string contentRootPath,
            ILogger logger) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            if (config == null) {
                throw new ArgumentNullException(nameof(config));
            }

            string directory = options.ResolveGeometryDirectory(contentRootPath);

            if (directory.Length == 0 || !Directory.Exists(directory)) {
                logger?.LogWarning(
                    "No collision geometry directory at '{GeometryDirectory}'. Every scene falls back to flat ground at y = 0.",
                    directory.Length == 0 ? "(unset)" : directory);
                return SceneGeometryLibrary.Empty;
            }

            SceneGeometryLibrary library = SceneGeometryLibrary.LoadFromDirectory(directory, config.Net.ServerTickInterval);
            logger?.LogInformation(
                "Loaded collision geometry for {SceneCount} scene(s) from '{GeometryDirectory}'.",
                library.Count,
                directory);
            return library;
        }

        private static long ReadWallClockUnixMs() {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
