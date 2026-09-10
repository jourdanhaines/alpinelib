using System;
using System.Globalization;

namespace AlpineLib.Server.Hosting {
    /// <summary>
    /// The one line a dedicated server writes to stdout once its socket is accepting traffic, and the
    /// parser for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A launcher that starts a server on an ephemeral port has no other way to learn which port the
    /// socket actually bound to, and no way to tell "process spawned" from "process listening". Both
    /// facts arrive together on this single line, so the launcher can tail stdout and stop guessing.
    /// </para>
    /// <para>
    /// The Unity-side launcher cannot reference this assembly — it targets net10.0 and the editor does
    /// not — so the wire format lives here as a contract both ends implement against rather than as a
    /// shared type. <see cref="Format"/> and <see cref="TryParse"/> must stay each other's inverse or
    /// the handshake hangs with no error on either side.
    /// </para>
    /// </remarks>
    public static class ReadinessLine {
        /// <summary>Marker every readiness line opens with, picked to be unmistakable in a log tail.</summary>
        public const string Prefix = "[ready]";

        private const string PortToken = "port=";
        private const int MaxPort = 65535;

        /// <summary>Renders the readiness line for a server listening on <paramref name="port"/>.</summary>
        public static string Format(int port) {
            if (port < 0 || port > MaxPort) {
                throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be in 0..65535.");
            }

            return Prefix + " " + PortToken + port.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads the port out of one line of server output. Returns false for anything that is not a
        /// readiness line, which is every other line the server prints.
        /// </summary>
        public static bool TryParse(string line, out int port) {
            port = 0;

            if (string.IsNullOrEmpty(line)) {
                return false;
            }

            string trimmed = line.Trim();
            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal)) {
                return false;
            }

            string remainder = trimmed.Substring(Prefix.Length).TrimStart();
            if (!remainder.StartsWith(PortToken, StringComparison.Ordinal)) {
                return false;
            }

            return TryParsePort(remainder.Substring(PortToken.Length).TrimEnd(), out port);
        }

        /// <summary>Parses the digits after the port token, rejecting signs, spaces and out-of-range values.</summary>
        private static bool TryParsePort(string value, out int port) {
            port = 0;

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)) {
                return false;
            }

            if (parsed > MaxPort) {
                return false;
            }

            port = parsed;
            return true;
        }
    }
}
