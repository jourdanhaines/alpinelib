using System;
using System.Numerics;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// The single home for the lossy encodings used on the wire. Writer and reader both route through
    /// here so an encode/decode pair can never drift apart, and so tests and the movement validator can
    /// reference the exact tolerance a round trip is allowed to introduce.
    /// </summary>
    public static class NetQuantization {
        /// <summary>Number of discrete yaw steps: a full turn mapped onto the whole ushort range.</summary>
        public const float YawSteps = 65536f;

        /// <summary>Degrees represented by one yaw step.</summary>
        public const float YawStepDegrees = 360f / YawSteps;

        /// <summary>Worst-case error in degrees introduced by a yaw encode/decode round trip.</summary>
        public const float YawToleranceDegrees = YawStepDegrees * 0.5f;

        /// <summary>Fixed-point scale for velocity components: 1/256 m/s resolution.</summary>
        public const float VelocityScale = 256f;

        /// <summary>Largest velocity component magnitude representable before clamping kicks in.</summary>
        public const float MaxVelocityComponent = 32767f / VelocityScale;

        /// <summary>Worst-case error per axis introduced by a velocity encode/decode round trip.</summary>
        public const float VelocityTolerance = 0.5f / VelocityScale;

        /// <summary>
        /// Maps any yaw in degrees onto [0, 360) and packs it into a ushort. Angles outside the range
        /// wrap rather than clamp, so a controller that keeps accumulating yaw never needs to normalise.
        /// </summary>
        public static ushort EncodeYaw(float degrees) {
            float normalized = degrees % 360f;
            if (normalized < 0f) {
                normalized += 360f;
            }

            int steps = (int)Math.Round(normalized * (YawSteps / 360f), MidpointRounding.AwayFromZero);
            return (ushort)(steps & 0xFFFF);
        }

        /// <summary>Unpacks a quantized yaw back into degrees within [0, 360).</summary>
        public static float DecodeYaw(ushort encoded) {
            return encoded * YawStepDegrees;
        }

        /// <summary>
        /// Packs one velocity axis into fixed point. Values beyond the representable range are clamped:
        /// a pawn moving faster than <see cref="MaxVelocityComponent"/> is already a validator problem,
        /// and clamping keeps the wire value finite instead of wrapping into a wildly wrong direction.
        /// </summary>
        public static short EncodeVelocityComponent(float value) {
            float scaled = value * VelocityScale;
            if (scaled >= short.MaxValue) {
                return short.MaxValue;
            }

            if (scaled <= short.MinValue) {
                return short.MinValue;
            }

            return (short)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        /// <summary>Unpacks one fixed-point velocity axis.</summary>
        public static float DecodeVelocityComponent(short encoded) {
            return encoded / VelocityScale;
        }

        /// <summary>Convenience round trip used by tests and by prediction code comparing wire fidelity.</summary>
        public static Vector3 QuantizeVelocity(Vector3 velocity) {
            return new Vector3(
                DecodeVelocityComponent(EncodeVelocityComponent(velocity.X)),
                DecodeVelocityComponent(EncodeVelocityComponent(velocity.Y)),
                DecodeVelocityComponent(EncodeVelocityComponent(velocity.Z)));
        }

        /// <summary>Convenience round trip for yaw, mirroring <see cref="QuantizeVelocity"/>.</summary>
        public static float QuantizeYaw(float degrees) {
            return DecodeYaw(EncodeYaw(degrees));
        }
    }
}
