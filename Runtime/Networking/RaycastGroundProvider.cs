using AlpineLib.Netcode.Replication;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// Answers the shared motor's one question about the world — how high the floor is under a point —
    /// by raycasting the engine's collision world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the listen host's ground seam. It exists so a host simulating its guests' pawns walks
    /// them over the same geometry the players see, instead of the flat plane a v1 dedicated server
    /// assumes. A dedicated server can never use it: <see cref="Physics"/> is engine state and the
    /// sample must happen on the main thread.
    /// </para>
    /// <para>
    /// The contract asks for purity, and a raycast against a scene whose geometry moves cannot deliver
    /// it. That is accepted for a listen host — the host is the authority, so its answer is by
    /// definition the correct one, and an owning client that predicted against a slightly different
    /// floor is corrected like any other divergence. Keep moving platforms off <see cref="groundMask"/>
    /// and the two agree in practice.
    /// </para>
    /// </remarks>
    public class RaycastGroundProvider : IGroundProvider {
        /// <summary>Height the probe starts from when the caller does not name one.</summary>
        public const float DefaultProbeHeight = 200f;

        /// <summary>Distance the probe travels when the caller does not name one.</summary>
        public const float DefaultProbeDistance = 400f;

        private readonly LayerMask _groundMask;
        private readonly float _probeHeight;
        private readonly float _probeDistance;
        private readonly float _fallbackHeight;

        /// <summary>Creates a provider probing the given layers, falling back to y = 0 off the map.</summary>
        public RaycastGroundProvider(LayerMask groundMask)
            : this(groundMask, DefaultProbeHeight, DefaultProbeDistance, 0f) { }

        /// <summary>Creates a provider with an explicit probe span and off-map floor height.</summary>
        /// <param name="groundMask">Layers that count as walkable ground. Must exclude pawn colliders.</param>
        /// <param name="probeHeight">World height the downward probe starts from.</param>
        /// <param name="probeDistance">How far the probe travels before giving up.</param>
        /// <param name="fallbackHeight">Height reported where the probe hits nothing.</param>
        public RaycastGroundProvider(LayerMask groundMask, float probeHeight, float probeDistance, float fallbackHeight) {
            _groundMask = groundMask;
            _probeHeight = probeHeight;
            _probeDistance = probeDistance;
            _fallbackHeight = fallbackHeight;
        }

        /// <inheritdoc />
        public float SampleHeight(float x, float z) {
            var origin = new Vector3(x, _probeHeight, z);

            bool hasHit = Physics.Raycast(
                origin, Vector3.down, out RaycastHit hit, _probeDistance, _groundMask, QueryTriggerInteraction.Ignore
            );

            return hasHit ? hit.point.y : _fallbackHeight;
        }
    }
}
