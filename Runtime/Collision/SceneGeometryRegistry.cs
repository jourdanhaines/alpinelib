using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Collision;
using UnityEngine;

namespace AlpineLib.Collision {
    /// <summary>
    /// Every scene's exported geometry the client can load, in one asset the session service holds.
    /// </summary>
    /// <remarks>
    /// The client's counterpart to the server's geometry directory. A session moves between the lobby
    /// scene and a match scene, and the collision world has to move with it; resolving by scene name from
    /// a single referenced asset keeps that swap a dictionary lookup instead of a resource load, and
    /// keeps the set of shipped worlds visible in one place rather than scattered across scenes.
    ///
    /// Built worlds are cached, because the world is immutable once constructed and a lobby-match-lobby
    /// round trip would otherwise decode and re-bucket the same geometry every time.
    /// </remarks>
    [CreateAssetMenu(fileName = "SceneGeometryRegistry", menuName = "AlpineLib/Collision/Scene Geometry Registry")]
    public class SceneGeometryRegistry : ScriptableObject {
        [Tooltip("One exported geometry asset per scene the client can play in. Order does not matter; scene names are the key.")]
        [SerializeField] private SceneGeometryAsset[] scenes = Array.Empty<SceneGeometryAsset>();

        private readonly Dictionary<string, CollisionWorld> worldCache =
            new Dictionary<string, CollisionWorld>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How many scenes have geometry authored.</summary>
        public int Count => scenes?.Length ?? 0;

        /// <summary>Finds the exported asset for a scene, or null when it has none.</summary>
        public SceneGeometryAsset FindAsset(string sceneName) {
            if (scenes == null || string.IsNullOrEmpty(sceneName)) {
                return null;
            }

            for (int sceneIndex = 0; sceneIndex < scenes.Length; sceneIndex++) {
                SceneGeometryAsset candidate = scenes[sceneIndex];

                if (candidate != null && string.Equals(candidate.SceneName, sceneName, StringComparison.OrdinalIgnoreCase)) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds — or returns the cached — collision world for a scene.
        /// </summary>
        /// <param name="sceneName">Scene to resolve.</param>
        /// <param name="tickIntervalSeconds">The session's fixed tick length, baked into the world.</param>
        /// <param name="world">The resolved world, valid only when this returns true.</param>
        /// <returns>False when no geometry was exported for that scene, leaving the caller to fall back to flat.</returns>
        /// <remarks>
        /// A cached world is only reused while it was built for the same tick length, because mover poses
        /// are phrased in ticks: handing back a world baked at thirty ticks a second to a session running
        /// at sixty would put every platform in the wrong place, silently, on both the render and the
        /// prediction path.
        /// </remarks>
        public bool TryResolveWorld(string sceneName, float tickIntervalSeconds, out CollisionWorld world) {
            world = null;

            if (string.IsNullOrEmpty(sceneName)) {
                return false;
            }

            if (TryReuseCachedWorld(sceneName, tickIntervalSeconds, out world)) {
                return true;
            }

            SceneGeometryAsset asset = FindAsset(sceneName);

            if (asset == null) {
                return false;
            }

            if (!TryDecode(asset, out SceneGeometry geometry)) {
                return false;
            }

            WarnOnHashDrift(asset, geometry);

            world = new CollisionWorld(geometry, tickIntervalSeconds);
            worldCache[sceneName] = world;
            return true;
        }

        /// <summary>Returns a previously built world when it was built for this tick length.</summary>
        private bool TryReuseCachedWorld(string sceneName, float tickIntervalSeconds, out CollisionWorld world) {
            if (!worldCache.TryGetValue(sceneName, out world) || world == null) {
                world = null;
                return false;
            }

            if (Mathf.Approximately(world.TickIntervalSeconds, tickIntervalSeconds)) {
                return true;
            }

            world = null;
            return false;
        }

        /// <summary>
        /// Decodes one asset's payload, reporting a corrupt or stale export rather than letting the
        /// exception out.
        /// </summary>
        /// <remarks>
        /// A payload written by an older format version is a build mismatch, not a bug in the caller, and
        /// the honest response is the same as an absent export: say so and fall back to flat ground, so a
        /// player with a stale client sees a wrong world instead of a dead one.
        /// </remarks>
        private static bool TryDecode(SceneGeometryAsset asset, out SceneGeometry geometry) {
            geometry = null;

            try {
                geometry = asset.Decode();
            } catch (Exception failure) {
                Debug.LogError($"SceneGeometryRegistry::TryDecode->{asset.name} could not be decoded: {failure.Message}");
                return false;
            }

            return geometry != null;
        }

        /// <summary>
        /// Notes when the hash stored beside the payload disagrees with the payload's own, which means the
        /// asset was edited by something other than the exporter.
        /// </summary>
        private static void WarnOnHashDrift(SceneGeometryAsset asset, SceneGeometry geometry) {
            if (asset.ContentHash == geometry.ContentHash) {
                return;
            }

            Debug.LogWarning(
                $"SceneGeometryRegistry::WarnOnHashDrift->{asset.name} records hash {asset.ContentHash} " +
                $"but its payload hashes to {geometry.ContentHash}. Re-export the scene.");
        }

        /// <summary>Drops every cached world. Used when geometry is re-exported in a running editor.</summary>
        public void ClearCache() {
            worldCache.Clear();
        }
    }
}
