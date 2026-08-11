using System;
using System.Collections.Generic;
using System.IO;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// Every scene's collision world the server knows about, keyed by scene name.
    /// </summary>
    /// <remarks>
    /// A session moves between scenes — lobby, match, back to lobby — and the geometry has to move with
    /// it. Loading every <c>.geo</c> in a directory once at boot and resolving by name on each phase
    /// change keeps the swap free of file I/O on the tick thread, and makes a missing export a warning at
    /// startup rather than a stall mid-match.
    ///
    /// Lookup is ordinal case-insensitive. Scene names come from an exported config on one side and a
    /// file name on the other, and the two have no common authority on casing; treating <c>GameScene</c>
    /// and <c>gamescene</c> as different worlds would be a silent flat-plane fallback nobody notices
    /// until pawns walk through a wall.
    /// </remarks>
    public sealed class SceneGeometryLibrary {
        /// <summary>Extension the exporter writes and the loader looks for.</summary>
        public const string GeometryFileExtension = ".geo";

        private const string GeometryFileSearchPattern = "*" + GeometryFileExtension;

        private readonly Dictionary<string, CollisionWorld> worldsBySceneName;

        /// <summary>Creates a library over an already-built set of worlds. The dictionary is held, not copied.</summary>
        public SceneGeometryLibrary(Dictionary<string, CollisionWorld> worldsBySceneName) {
            this.worldsBySceneName = worldsBySceneName
                ?? new Dictionary<string, CollisionWorld>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A library that resolves nothing. What a server without a geometry directory runs on: every
        /// scene falls back to <see cref="CollisionWorld.Flat"/>, and it says so in the log.
        /// </summary>
        public static SceneGeometryLibrary Empty =>
            new SceneGeometryLibrary(new Dictionary<string, CollisionWorld>(StringComparer.OrdinalIgnoreCase));

        /// <summary>How many scenes resolved.</summary>
        public int Count => worldsBySceneName.Count;

        /// <summary>
        /// Reads every <c>.geo</c> file in a directory and builds a world per scene. A missing directory
        /// yields <see cref="Empty"/>; an unreadable file is skipped rather than taking the whole library
        /// down with it.
        /// </summary>
        /// <param name="directoryPath">Directory holding the exported <c>.geo</c> files.</param>
        /// <param name="tickIntervalSeconds">The session's fixed tick length, baked into every world.</param>
        public static SceneGeometryLibrary LoadFromDirectory(string directoryPath, float tickIntervalSeconds) {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) {
                return Empty;
            }

            string[] filePaths = Directory.GetFiles(directoryPath, GeometryFileSearchPattern);
            Array.Sort(filePaths, StringComparer.Ordinal);

            var worlds = new Dictionary<string, CollisionWorld>(StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in filePaths) {
                if (!TryLoadFile(filePath, tickIntervalSeconds, out string sceneName, out CollisionWorld world)) {
                    continue;
                }

                worlds[sceneName] = world;
            }

            return new SceneGeometryLibrary(worlds);
        }

        /// <summary>Finds the world exported for a scene.</summary>
        /// <returns>False when nothing was exported for that name, leaving the caller to fall back to flat.</returns>
        public bool TryResolve(string sceneName, out CollisionWorld world) {
            if (string.IsNullOrEmpty(sceneName)) {
                world = null;
                return false;
            }

            return worldsBySceneName.TryGetValue(sceneName, out world);
        }

        /// <summary>
        /// Decodes one <c>.geo</c> file into a world. Failures are swallowed on purpose: a server that
        /// refused to boot because one stale export in the directory was written by an older format would
        /// be down for a reason nobody deployed, whereas a scene that quietly fails to resolve falls back
        /// to flat ground and says so at the resolve site.
        /// </summary>
        private static bool TryLoadFile(
            string filePath,
            float tickIntervalSeconds,
            out string sceneName,
            out CollisionWorld world) {
            sceneName = null;
            world = null;

            try {
                byte[] payload = File.ReadAllBytes(filePath);
                SceneGeometry geometry = SceneGeometryCodec.Decode(payload);
                sceneName = ResolveSceneName(geometry, filePath);
                world = new CollisionWorld(geometry, tickIntervalSeconds);
                return true;
            } catch (NetProtocolException) {
                return false;
            } catch (IOException) {
                return false;
            } catch (UnauthorizedAccessException) {
                return false;
            }
        }

        /// <summary>
        /// The scene a file belongs to. The name baked into the export wins; the file name is the fallback
        /// for a hand-renamed or hand-placed <c>.geo</c>, which is exactly how an operator swaps a world on
        /// a deployed server without a Unity install.
        /// </summary>
        private static string ResolveSceneName(SceneGeometry geometry, string filePath) {
            if (!string.IsNullOrEmpty(geometry.SceneName)) {
                return geometry.SceneName;
            }

            return Path.GetFileNameWithoutExtension(filePath);
        }
    }
}
