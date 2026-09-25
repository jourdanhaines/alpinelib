using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlpineLib.Netcode.Appearance;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// Reads the editor-exported appearance catalog off disk, beside the session config it belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fatal like <see cref="SessionConfigLoader"/>: a server without the catalog would refuse every
    /// outfit request, which a player sees as clothes that never change with nothing in a log to say
    /// why. Refusing to start names the file instead.
    /// </para>
    /// <para>
    /// Parsing is forgiving the same ways the session config is — case-insensitive keys, comments and
    /// trailing commas — but the rows themselves are held to <see cref="AppearanceCatalogTable"/>'s rules.
    /// </para>
    /// </remarks>
    public static class AppearanceCatalogLoader {
        /// <summary>Folder inside a config directory that the catalog lives in.</summary>
        public const string FolderName = "appearance";

        /// <summary>Name the exporter gives the catalog file.</summary>
        public const string FileName = "appearance-catalog.json";

        private static readonly JsonSerializerOptions ReadOptions = BuildReadOptions();

        /// <summary>Where the catalog lives, given the session config path the host already resolved.</summary>
        /// <remarks>
        /// Derived from the session config so <c>--config</c> moves both files together, and a deployment
        /// cannot serve one directory's session rules with another's catalog.
        /// </remarks>
        public static string ResolvePath(string sessionConfigPath) {
            if (string.IsNullOrWhiteSpace(sessionConfigPath)) {
                throw new InvalidOperationException(
                    "No session config path was configured, so the appearance catalog beside it cannot be found. Pass --config <dir>.");
            }

            string configDirectory = Path.GetDirectoryName(Path.GetFullPath(sessionConfigPath)) ?? string.Empty;
            return Path.Combine(configDirectory, FolderName, FileName);
        }

        /// <summary>Loads and validates the catalog at the given path.</summary>
        /// <exception cref="InvalidOperationException">The file is missing, empty, malformed or breaks the catalog rules.</exception>
        public static AppearanceCatalogTable LoadFromFile(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                throw new InvalidOperationException("No appearance catalog path was given.");
            }

            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath)) {
                throw new InvalidOperationException(
                    "Appearance catalog '" + fullPath + "' was not found. Export it from Unity with the server config.");
            }

            return Parse(File.ReadAllText(fullPath), fullPath);
        }

        /// <summary>Maps and validates an already-read document. The path is carried for diagnostics only.</summary>
        /// <exception cref="InvalidOperationException">The JSON is empty, malformed or breaks the catalog rules.</exception>
        public static AppearanceCatalogTable Parse(string json, string sourcePath) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new InvalidOperationException("Appearance catalog '" + (sourcePath ?? string.Empty) + "' is empty.");
            }

            AppearanceCatalogDocument document = Deserialize(json, sourcePath);

            if (document == null) {
                throw new InvalidOperationException(
                    "Appearance catalog '" + (sourcePath ?? string.Empty) + "' deserialised to nothing.");
            }

            return document.ToTable(sourcePath);
        }

        private static AppearanceCatalogDocument Deserialize(string json, string sourcePath) {
            try {
                return JsonSerializer.Deserialize<AppearanceCatalogDocument>(json, ReadOptions);
            }
            catch (JsonException error) {
                throw new InvalidOperationException(
                    "Appearance catalog '" + (sourcePath ?? string.Empty) + "' is not valid JSON: " + error.Message, error);
            }
        }

        private static JsonSerializerOptions BuildReadOptions() {
            JsonSerializerOptions options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
