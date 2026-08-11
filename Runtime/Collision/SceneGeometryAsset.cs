using System;
using AlpineLib.Netcode.Collision;
using UnityEngine;

namespace AlpineLib.Collision {
    /// <summary>
    /// One scene's exported collision geometry, as an asset the client ships with its build.
    /// </summary>
    /// <remarks>
    /// The bytes here are byte-for-byte the same payload the exporter writes to the server's
    /// <c>.geo</c> file — one encode, two destinations. Storing the encoded form rather than a
    /// Unity-serialized shape list is the entire point: a ScriptableObject with authored fields would be
    /// a second representation that could drift from the server's, whereas an opaque payload can only
    /// ever decode to what the server decoded.
    ///
    /// <see cref="contentHash"/> is stored alongside so a mismatch can be reported at load without
    /// decoding, and so a human can eyeball a build against a deployed server.
    /// </remarks>
    public class SceneGeometryAsset : ScriptableObject {
        [Tooltip("Unity scene this geometry was exported from. The key sessions resolve by.")]
        [SerializeField] private string sceneName = string.Empty;

        [Tooltip("Encoded geometry — identical to the bytes written to the server's .geo file. Do not hand-edit.")]
        [SerializeField] private byte[] payload = Array.Empty<byte>();

        [Tooltip("FNV-1a over the exported shapes and movers. Must match the server's copy.")]
        [SerializeField] private uint contentHash;

        /// <summary>Scene this geometry belongs to.</summary>
        public string SceneName => sceneName;

        /// <summary>Encoded geometry bytes.</summary>
        public byte[] Payload => payload;

        /// <summary>Content hash recorded at export time.</summary>
        public uint ContentHash => contentHash;

        /// <summary>Decodes the payload into the shared geometry the collision world is built from.</summary>
        /// <returns>The decoded geometry, or null when the asset carries no payload.</returns>
        public SceneGeometry Decode() {
            if (payload == null || payload.Length == 0) {
                Debug.LogError($"SceneGeometryAsset::Decode->{name} has no exported payload.");
                return null;
            }

            return SceneGeometryCodec.Decode(payload);
        }

        /// <summary>Overwrites the asset's contents. Called by the exporter only.</summary>
        public void SetExport(string exportedSceneName, byte[] exportedPayload, uint exportedContentHash) {
            sceneName = exportedSceneName ?? string.Empty;
            payload = exportedPayload ?? Array.Empty<byte>();
            contentHash = exportedContentHash;
        }
    }
}
