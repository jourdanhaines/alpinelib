using System;
using UnityEngine;
using KitVector3 = System.Numerics.Vector3;

namespace AlpineLib.Procedural.Kits {
    /// <summary>Converts poses between a layout's kit frame and Unity's local space.</summary>
    /// <remarks>
    /// <see cref="KitAxisConvention.BlenderZUp"/> is the frame an FBX exported from Blender lands in: kit
    /// (x, y, z) is Unity (−x, z, −y), and a counter-clockwise kit yaw θ is a Unity yaw of −θ about +Y.
    /// </remarks>
    public static class KitAxes {
        /// <summary>A kit-frame position in Unity local space.</summary>
        public static Vector3 ToUnityPosition(KitVector3 kit, KitAxisConvention convention) {
            if (convention == KitAxisConvention.UnityNative) return new Vector3(kit.X, kit.Y, kit.Z);

            return new Vector3(-kit.X, kit.Z, -kit.Y);
        }

        /// <summary>A kit yaw in degrees as a Unity rotation about +Y.</summary>
        public static Quaternion ToUnityRotation(float kitYawDegrees, KitAxisConvention convention) {
            float unityYaw = convention == KitAxisConvention.UnityNative ? kitYawDegrees : -kitYawDegrees;
            return Quaternion.Euler(0f, unityYaw, 0f);
        }

        /// <summary>A Unity local position in the kit frame; inverse of <see cref="ToUnityPosition"/>.</summary>
        public static KitVector3 ToKitPosition(Vector3 unity, KitAxisConvention convention) {
            if (convention == KitAxisConvention.UnityNative) return new KitVector3(unity.x, unity.y, unity.z);

            return new KitVector3(-unity.x, -unity.z, unity.y);
        }

        /// <summary>
        /// The yaw of a Unity rotation's forward in kit degrees, in (−180, 180]; inverse of
        /// <see cref="ToUnityRotation"/> for yaw-only rotations.
        /// </summary>
        public static float ToKitYaw(Quaternion rotation, KitAxisConvention convention) {
            float unityYaw = UnityYawDegrees(rotation * Vector3.forward);
            float kitYaw = convention == KitAxisConvention.UnityNative ? unityYaw : -unityYaw;
            return NormaliseDegrees(kitYaw);
        }

        /// <summary>A kit pose as a Unity local position and rotation.</summary>
        public static (Vector3 Position, Quaternion Rotation) ToUnityPose(KitPose pose, KitAxisConvention convention) {
            return (ToUnityPosition(pose.Position, convention), ToUnityRotation(pose.YawDegrees, convention));
        }

        /// <summary>Unity yaw of a direction about +Y, degrees, 0 along +Z; zero for a vertical direction.</summary>
        public static float UnityYawDegrees(Vector3 direction) {
            if (Mathf.Approximately(direction.x, 0f) && Mathf.Approximately(direction.z, 0f)) return 0f;

            return (float)(Math.Atan2(direction.x, direction.z) * (180.0 / Math.PI));
        }

        /// <summary>Wraps an angle into (−180, 180].</summary>
        public static float NormaliseDegrees(float degrees) {
            float wrapped = degrees % 360f;
            if (wrapped > 180f) return wrapped - 360f;
            if (wrapped <= -180f) return wrapped + 360f;

            return wrapped;
        }
    }
}
