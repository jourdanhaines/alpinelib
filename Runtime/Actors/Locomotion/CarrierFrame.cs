using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// Converts between the world and the frame of a carrier transform: a rigid rotate-and-translate,
    /// never a scale.
    /// </summary>
    /// <remarks>
    /// Positions use the carrier's full rotation so a pawn on a graded deck keeps its place on the deck.
    /// Velocities and yaw use only the carrier's heading, because the pawn itself stays world-upright and
    /// its planar velocity has no vertical part. A null carrier is the world frame.
    /// </remarks>
    public static class CarrierFrame {
        /// <summary>How far each axis of a carrier's lossy scale may sit from one and still be a frame.</summary>
        public const float ScaleTolerance = 1e-3f;

        public static Vector3 ToWorldPoint(Transform carrier, Vector3 local) {
            if (carrier == null) return local;

            return carrier.position + carrier.rotation * local;
        }

        public static Vector3 ToLocalPoint(Transform carrier, Vector3 world) {
            if (carrier == null) return world;

            return Quaternion.Inverse(carrier.rotation) * (world - carrier.position);
        }

        /// <summary>Rotates a planar vector out of the carrier's yaw frame into the world.</summary>
        public static Vector3 ToWorldPlanar(Transform carrier, Vector3 local) {
            return YawRotation(carrier) * local;
        }

        /// <summary>Rotates a planar vector from the world into the carrier's yaw frame.</summary>
        public static Vector3 ToLocalPlanar(Transform carrier, Vector3 world) {
            return Quaternion.Inverse(YawRotation(carrier)) * world;
        }

        /// <summary>The carrier's yaw in degrees, zero for the world.</summary>
        public static float Heading(Transform carrier) {
            return carrier != null ? carrier.eulerAngles.y : 0f;
        }

        public static Quaternion YawRotation(Transform carrier) {
            return Quaternion.Euler(0f, Heading(carrier), 0f);
        }

        /// <summary>
        /// Whether a transform can be a frame: present and at unit world scale. A scaled carrier would
        /// store scaled metres and report them as real ones.
        /// </summary>
        public static bool IsUsable(Transform carrier) {
            if (carrier == null) return false;

            Vector3 scale = carrier.lossyScale;
            return Mathf.Abs(scale.x - 1f) <= ScaleTolerance
                && Mathf.Abs(scale.y - 1f) <= ScaleTolerance
                && Mathf.Abs(scale.z - 1f) <= ScaleTolerance;
        }
    }
}
