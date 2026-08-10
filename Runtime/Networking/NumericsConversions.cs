using UnityEngine;
using Numerics = System.Numerics;

namespace AlpineLib.Networking {
    /// <summary>
    /// Translates between the engine's vectors and the <see cref="System.Numerics"/> vectors the shared
    /// netcode assemblies speak.
    /// </summary>
    /// <remarks>
    /// The netcode assembly compiles without any engine reference, so every value that crosses the wire
    /// is a <see cref="System.Numerics.Vector3"/>. That boundary is crossed constantly — every predicted
    /// state applied to a transform, every input built from a stick — and doing it inline would spread
    /// component-by-component constructors through every adapter. Conversions are exact: both types are
    /// three (or two) single-precision floats in the same order, so nothing is lost either way.
    /// </remarks>
    public static class NumericsConversions {
        /// <summary>Converts a shared vector to an engine vector.</summary>
        public static Vector3 ToUnity(this Numerics.Vector3 value) {
            return new Vector3(value.X, value.Y, value.Z);
        }

        /// <summary>Converts an engine vector to a shared vector.</summary>
        public static Numerics.Vector3 ToNumerics(this Vector3 value) {
            return new Numerics.Vector3(value.x, value.y, value.z);
        }

        /// <summary>Converts a shared two-component vector to an engine vector.</summary>
        public static Vector2 ToUnity(this Numerics.Vector2 value) {
            return new Vector2(value.X, value.Y);
        }

        /// <summary>Converts an engine two-component vector to a shared vector.</summary>
        public static Numerics.Vector2 ToNumerics(this Vector2 value) {
            return new Numerics.Vector2(value.x, value.y);
        }

        /// <summary>
        /// Flattens an engine vector into the ground-plane pair the netcode uses for move intent, where
        /// X is world east and Y is world north.
        /// </summary>
        /// <remarks>
        /// Movement intent travels as two components rather than three because the shared motor derives
        /// vertical motion itself. The mapping — engine Z into shared Y — is the one place that choice is
        /// written down, so an adapter never has to remember which axis was dropped.
        /// </remarks>
        public static Numerics.Vector2 ToPlanarNumerics(this Vector3 value) {
            return new Numerics.Vector2(value.x, value.z);
        }

        /// <summary>Expands a ground-plane pair back into an engine vector with no vertical component.</summary>
        public static Vector3 ToPlanarUnity(this Numerics.Vector2 value) {
            return new Vector3(value.X, 0f, value.Y);
        }
    }
}
