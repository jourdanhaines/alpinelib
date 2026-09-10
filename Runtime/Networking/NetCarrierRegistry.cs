using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// The one place a carrier id from the wire is turned back into the object it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static because the mapping is a property of the loaded scene rather than of any one component:
    /// every remote pawn's driver needs it every frame, and threading a lookup service through the pawn
    /// prefab, the possession flow and the session would buy nothing but plumbing. The cost of that
    /// choice is domain-reload survival, which is why <see cref="Reset"/> runs at subsystem
    /// registration — with reload disabled in the editor a stale carrier from the previous play session
    /// would otherwise still answer to its id.
    /// </para>
    /// <para>
    /// A duplicate id is a scene-authoring mistake with a silent failure mode: riders of one carrier
    /// would be posed against another, somewhere else entirely. It is reported and the first
    /// registration keeps the id, so the error names the object that lost rather than leaving which one
    /// won to load order.
    /// </para>
    /// </remarks>
    public static class NetCarrierRegistry {
        private static readonly Dictionary<ushort, NetCarrier> CarriersById = new Dictionary<ushort, NetCarrier>();

        /// <summary>Carriers currently registered.</summary>
        public static int Count => CarriersById.Count;

        /// <summary>
        /// Publishes a carrier under its id.
        /// </summary>
        /// <returns>False when the id was already taken, in which case nothing was registered.</returns>
        public static bool Register(NetCarrier carrier) {
            if (carrier == null) return false;

            if (CarriersById.TryGetValue(carrier.CarrierId, out NetCarrier existing) && existing != carrier) {
                Debug.LogError($"NetCarrierRegistry::Register->Carrier id {carrier.CarrierId} is already held by '{existing.name}'; '{carrier.name}' will not be resolvable and its riders will be posed against the wrong object.");
                return false;
            }

            CarriersById[carrier.CarrierId] = carrier;
            return true;
        }

        /// <summary>
        /// Withdraws a carrier, leaving an id claimed by a different object alone.
        /// </summary>
        public static void Unregister(NetCarrier carrier) {
            if (carrier == null) return;

            if (!CarriersById.TryGetValue(carrier.CarrierId, out NetCarrier existing) || existing != carrier) {
                return;
            }

            CarriersById.Remove(carrier.CarrierId);
        }

        /// <summary>Finds the carrier an id names.</summary>
        /// <returns>False when no enabled carrier currently answers to that id.</returns>
        public static bool TryResolve(ushort carrierId, out NetCarrier carrier) {
            return CarriersById.TryGetValue(carrierId, out carrier) && carrier != null;
        }

        /// <summary>Empties the registry before the first scene of a play session loads.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() {
            CarriersById.Clear();
        }
    }
}
