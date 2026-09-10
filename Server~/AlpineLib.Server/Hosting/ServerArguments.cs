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
    /// exists to make loud.
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
        private int ReadOne(string[] args, int index) {
            string flag = args[index] ?? string.Empty;

            switch (flag) {
                case PortFlag:
                    Port = ReadPort(args, index);
                    return index + 1;
                case ConfigFlag:
                    ConfigDirectory = ReadValue(args, index);
                    return index + 1;
                case IdleExitFlag:
                    IdleExitSeconds = ReadNonNegative(args, index);
                    return index + 1;
                case MaxSessionsFlag:
                    MaxSessions = ReadPositive(args, index);
                    return index + 1;
                default:
                    throw new ArgumentException("Unknown argument '" + flag + "'. " + Usage, nameof(args));
            }
        }

        private static string ReadValue(string[] args, int index) {
            if (index + 1 >= args.Length) {
                throw new ArgumentException(args[index] + " needs a value. " + Usage, nameof(args));
            }

            string value = args[index + 1];

            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(args[index] + " needs a value. " + Usage, nameof(args));
            }

            return value;
        }

        private static int ReadInteger(string[] args, int index) {
            string value = ReadValue(args, index);

            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed)) {
                throw new ArgumentException(args[index] + " needs a whole number, not '" + value + "'. " + Usage, nameof(args));
            }

            return parsed;
        }

        private static int ReadPort(string[] args, int index) {
            int port = ReadInteger(args, index);

            if (port < 0 || port > MaxPort) {
                throw new ArgumentException(PortFlag + " must be in 0..65535. " + Usage, nameof(args));
            }

            return port;
        }

        private static int ReadNonNegative(string[] args, int index) {
            int value = ReadInteger(args, index);

            if (value < 0) {
                throw new ArgumentException(args[index] + " cannot be negative. " + Usage, nameof(args));
            }

            return value;
        }

        private static int ReadPositive(string[] args, int index) {
            int value = ReadInteger(args, index);

            if (value < 1) {
                throw new ArgumentException(args[index] + " must be at least 1. " + Usage, nameof(args));
            }

            return value;
        }
    }
}
