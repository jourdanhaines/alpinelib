using System;
using AlpineLib.Chat;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Server.Sessions.Spawning;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// Everything one exported config file turns into: the session rule set, the networking settings, the
    /// chat policy and the spawn rules, resolved once at startup and shared by every session afterwards.
    /// </summary>
    /// <remarks>
    /// The four travel together because they came from one authored asset and any two of them out of
    /// step is a bug: a send rate that disagrees with the tick rate, a chat cap the clients were never
    /// told about, a prefab id no client registry has. Passing the bundle rather than four loose objects
    /// makes that impossible to do by accident.
    /// </remarks>
    public sealed class ServerConfigBundle {
        /// <summary>
        /// Binds the four together, refusing a networking section that cannot simulate correctly.
        /// </summary>
        /// <remarks>
        /// This constructor is the one gate every loaded config passes through, so it is where the
        /// networking rates are checked. Refusing at startup costs a deployment one clear log line;
        /// accepting a mismatched send rate costs every player on the server a pawn that rubber-bands and
        /// nobody a single error to go on.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The networking rates are inconsistent.</exception>
        public ServerConfigBundle(
            SessionConfigData session,
            NetConfig net,
            ChatSettings chat,
            SpawnSettings spawn,
            string sourcePath) {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Net = net ?? throw new ArgumentNullException(nameof(net));
            Chat = chat ?? throw new ArgumentNullException(nameof(chat));
            Spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
            SourcePath = sourcePath ?? string.Empty;

            Net.Validate();
        }

        /// <summary>The rule set handed verbatim to every client that joins a session.</summary>
        public SessionConfigData Session { get; }

        /// <summary>Transport, tick and snapshot settings for the one socket this process binds.</summary>
        public NetConfig Net { get; }

        /// <summary>Policy for the per-session chat pipelines.</summary>
        public ChatSettings Chat { get; }

        /// <summary>What an arriving player is given a body as, and where it appears.</summary>
        public SpawnSettings Spawn { get; }

        /// <summary>Where the configuration was read from, for logs and for the admin surface.</summary>
        public string SourcePath { get; }
    }
}
