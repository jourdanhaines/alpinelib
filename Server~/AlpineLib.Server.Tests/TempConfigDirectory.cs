using System;
using System.IO;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A throwaway <c>--config</c> directory holding one exported session config, cleaned up with the
    /// test that made it.
    /// </summary>
    internal sealed class TempConfigDirectory : IDisposable {
        private TempConfigDirectory(string path) {
            Path = path;
        }

        /// <summary>The directory a launcher would pass to <c>--config</c>.</summary>
        public string Path { get; }

        /// <summary>Writes the given JSON as the directory's session config.</summary>
        public static TempConfigDirectory Create(string json) {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "alpine-config-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(path);
            File.WriteAllText(System.IO.Path.Combine(path, "session-config.json"), json);
            return new TempConfigDirectory(path);
        }

        /// <inheritdoc />
        public void Dispose() {
            try {
                Directory.Delete(Path, true);
            }
            catch (IOException) {
                // A leftover temp directory is noise, not a failure; the operating system reclaims it.
            }
        }
    }
}
