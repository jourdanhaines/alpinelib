using System;
using System.Globalization;

namespace AlpineLib.Server.Hosting {
    /// <summary>
    /// The command line a dedicated server is launched with, parsed into the four things a launcher or an
    /// operator ever needs to say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately tiny, and deliberately not a configuration binder. Everything about how the game
    /// plays is authored in Unity and exported; what is left on the command line is where that export
    /// lives and how this particular process should behave — which port, how many sessions, whether to
    /// stop itself when nobody is connected.
    /// </para>
    /// <para>
    /// <b>Absent is not zero.</b> Every value is nullable so that a flag nobody passed leaves whatever
    /// <c>appsettings.json</c> said standing. Passing <c>--port 0</c> is a real instruction — bind an
    /// ephemeral port and announce it — and has to be distinguishable from not passing the flag at all.
    /// </para>
    /// <para>
    /// An unrecognised argument is refused rather than ignored. A typo in a launcher's command line would
    /// otherwise produce a server that quietly ran on the wrong port, which is the failure this class
    /// exists to make loud. Both spellings of a value are read — <c>--config /srv/x</c> and the GNU
    /// <c>--config=/srv/x</c> — because an operator who types the second should not be told the flag
    /// itself is unknown.
    /// </para>
    /// </remarks>
    public sealed class ServerArguments {
        /// <summary>Flag naming the UDP port to bind. Zero asks the operating system for a free one.</summary>
        public const string PortFlag = "--port";

        /// <summary>Flag naming the directory holding the exported config and geometry.</summary>
        public const string ConfigFlag = "--config";

        /// <summary>Flag naming how long the process may sit with no connections before it stops.</summary>
        public const string IdleExitFlag = "--idle-exit-seconds";

        /// <summary>Flag naming how many sessions this process will host at once.</summary>
        public const string MaxSessionsFlag = "--max-sessions";

        /// <summary>What the process prints when it is handed something it does not understand.</summary>
        public const string Usage =
            "Usage: [--port <0-65535>] [--config <directory>] [--idle-exit-seconds <seconds>] [--max-sessions <count>]";

        private const int MaxPort = 65535;
        private const string FlagPrefix = "--";
        private const string ArgumentsParameterName = "args";

        private static readonly string[] KnownFlags = { PortFlag, ConfigFlag, IdleExitFlag, MaxSessionsFlag };

        /// <summary>Port to bind, or null when the command line did not say. Zero means ephemeral.</summary>
        public int? Port { get; private set; }

        /// <summary>Directory holding <c>session-config.json</c> and <c>geometry/</c>, or null.</summary>
        public string ConfigDirectory { get; private set; }

        /// <summary>Idle window in seconds, or null. Zero disables the idle exit.</summary>
        public int? IdleExitSeconds { get; private set; }

        /// <summary>Session cap, or null when the command line did not say.</summary>
        public int? MaxSessions { get; private set; }

        /// <summary>Reads a command line. Never returns null; an empty line is a valid, all-absent result.</summary>
        /// <exception cref="ArgumentException">An argument is unknown, malformed or out of range.</exception>
        public static ServerArguments Parse(string[] args) {
            ServerArguments parsed = new ServerArguments();

            if (args == null) {
                return parsed;
            }

            for (int argumentIndex = 0; argumentIndex < args.Length; argumentIndex++) {
                argumentIndex = parsed.ReadOne(args, argumentIndex);
            }

            return parsed;
        }

        /// <summary>Reads the flag at <paramref name="index"/> and returns the index of its last token.</summary>
        /// <remarks>
        /// The flag is split off its value before anything else happens, so an attached value and a
        /// following one meet the same checks. An unknown flag is refused before a value is read for it:
        /// a typo must be reported as a typo rather than as the missing value it appears to need.
        /// </remarks>
        private int ReadOne(string[] args, int index) {
            string token = args[index] ?? string.Empty;
            int separatorIndex = token.IndexOf('=');
            string flag = separatorIndex < 0 ? token : token.Substring(0, separatorIndex);

            if (Array.IndexOf(KnownFlags, flag) < 0) {
                throw new ArgumentException("Unknown argument '" + token + "'. " + Usage, ArgumentsParameterName);
            }

            if (separatorIndex < 0) {
                Apply(flag, ReadFollowingValue(args, index, flag));
                return index + 1;
            }

            Apply(flag, ReadAttachedValue(token, flag, separatorIndex));
            return index;
        }

        /// <summary>Stores one flag's value, now that where on the line it was written no longer matters.</summary>
        private void Apply(string flag, string value) {
            switch (flag) {
                case PortFlag:
                    Port = ReadPort(flag, value);
                    return;
                case ConfigFlag:
                    ConfigDirectory = value;
                    return;
                case IdleExitFlag:
                    IdleExitSeconds = ReadNonNegative(flag, value);
                    return;
                case MaxSessionsFlag:
                    MaxSessions = ReadPositive(flag, value);
                    return;
                default:
                    throw new ArgumentException("Unknown argument '" + flag + "'. " + Usage, ArgumentsParameterName);
            }
        }

        /// <summary>The token after a flag, refused when it is absent, blank, or another flag.</summary>
        /// <remarks>
        /// Refusing a value that reads as a flag is what turns <c>--config --port 9051</c> into a message
        /// about the directory nobody named, instead of a server whose config directory is called
        /// "--port" and which then chokes on the 9051 it no longer has a flag for.
        /// </remarks>
        private static string ReadFollowingValue(string[] args, int index, string flag) {
            if (index + 1 >= args.Length) {
                throw new ArgumentException(flag + " needs a value. " + Usage, ArgumentsParameterName);
            }

            string value = args[index + 1] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(flag + " needs a value. " + Usage, ArgumentsParameterName);
            }

            if (value.StartsWith(FlagPrefix, StringComparison.Ordinal)) {
                throw new ArgumentException(
                    flag + " needs a value, not the flag '" + value + "'. " + Usage, ArgumentsParameterName);
            }

            return value;
        }

        /// <summary>The value written into the flag itself, as in <c>--config=/srv/alpine</c>.</summary>
        private static string ReadAttachedValue(string token, string flag, int separatorIndex) {
            string value = token.Substring(separatorIndex + 1);

            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(flag + " needs a value. " + Usage, ArgumentsParameterName);
            }

            return value;
        }

        private static int ReadInteger(string flag, string value) {
            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed)) {
                throw new ArgumentException(
                    flag + " needs a whole number, not '" + value + "'. " + Usage, ArgumentsParameterName);
            }

            return parsed;
        }

        private static int ReadPort(string flag, string value) {
            int port = ReadInteger(flag, value);

            if (port < 0 || port > MaxPort) {
                throw new ArgumentException(PortFlag + " must be in 0..65535. " + Usage, ArgumentsParameterName);
            }

            return port;
        }

        private static int ReadNonNegative(string flag, string value) {
            int parsed = ReadInteger(flag, value);

            if (parsed < 0) {
                throw new ArgumentException(flag + " cannot be negative. " + Usage, ArgumentsParameterName);
            }

            return parsed;
        }

        private static int ReadPositive(string flag, string value) {
            int parsed = ReadInteger(flag, value);

            if (parsed < 1) {
                throw new ArgumentException(flag + " must be at least 1. " + Usage, ArgumentsParameterName);
            }

            return parsed;
        }
    }
}
