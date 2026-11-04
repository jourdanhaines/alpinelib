using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// Maps a Unity world position to absolute world xz (metres, double), the frame that never moves when a
    /// floating origin rebases, so seeded sky layouts line up for every client.
    /// </summary>
    public interface IAbsoluteFrame {
        /// <summary>World y of the ground plane sky silhouettes stand on.</summary>
        float GroundY { get; }

        /// <summary>Absolute xz of <paramref name="worldPosition"/>; false when the frame cannot say yet.</summary>
        bool TryToAbsolute(Vector3 worldPosition, out double x, out double z);
    }
}
