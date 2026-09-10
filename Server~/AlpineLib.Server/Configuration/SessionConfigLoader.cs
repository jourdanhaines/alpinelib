using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// Reads the editor-exported session configuration off disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A missing or unreadable config is fatal and says so at startup. The alternative — booting on
    /// built-in defaults — produces a server that accepts connections and then plays by rules no client
    /// was shipped with, which is far harder to diagnose than a process that refuses to start.
    /// </para>
    /// <para>
    /// Parsing is deliberately forgiving in the two ways an exported file drifts in practice: keys may
    /// differ in case, and a file written by an older editor may be missing keys the server has since
    /// learned about. Missing keys fall back to the documented defaults; unknown keys are ignored so a
    /// newer editor's export still boots an older server.
    /// </para>
    /// </remarks>
    public static class SessionConfigLoader {
        /// <summary>Name the loader expects the exported config to carry inside a config directory.</summary>
        public const string DefaultFileName = "session-config.json";

        private static readonly JsonSerializerOptions ReadOptions = BuildReadOptions();

        /// <summary>Loads and maps the configuration at the given path.</summary>
        /// <exception cref="InvalidOperationException">The file is missing, empty or malformed.</exception>
        public static ServerConfigBundle LoadFromFile(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                throw new InvalidOperationException(
                    "No session config path was configured. Pass --config <dir> or set AlpineServer:SessionConfigPath.");
            }

            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath)) {
                throw new InvalidOperationException(
                    "Session config '" + fullPath + "' was not found. Export it from Unity with AlpineLib/Editor/Export Session Config.");
            }

            return Parse(File.ReadAllText(fullPath), fullPath);
        }

        /// <summary>Maps an already-read document. The path is carried for diagnostics only.</summary>
        /// <exception cref="InvalidOperationException">The JSON is empty or malformed.</exception>
        public static ServerConfigBundle Parse(string json, string sourcePath) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new InvalidOperationException("Session config '" + (sourcePath ?? string.Empty) + "' is empty.");
            }

            ServerConfigDocument document = Deserialize(json, sourcePath);

            if (document == null) {
                throw new InvalidOperationException("Session config '" + (sourcePath ?? string.Empty) + "' deserialised to nothing.");
            }

            return document.ToBundle(sourcePath);
        }

        private static ServerConfigDocument Deserialize(string json, string sourcePath) {
            try {
                return JsonSerializer.Deserialize<ServerConfigDocument>(json, ReadOptions);
            }
            catch (JsonException error) {
                throw new InvalidOperationException(
                    "Session config '" + (sourcePath ?? string.Empty) + "' is not valid JSON: " + error.Message, error);
            }
        }

        private static JsonSerializerOptions BuildReadOptions() {
            JsonSerializerOptions options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            // The exporter writes enums as their numeric values; a hand-edited file is far more likely to
            // spell them out. This converter takes both, so neither producer has to know about the other.
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
