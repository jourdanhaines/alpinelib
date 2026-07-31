using UnityEngine;

namespace AlpineLib.Pointer {
    /// <summary>
    /// Device abstraction behind <see cref="PointerService"/>: turns whatever the player is aiming with
    /// (mouse, gamepad stick, touch) into a world ray, a world position, and an interact press.
    /// </summary>
    public interface IPointerSource {
        /// <summary>
        /// Ray pointing from the viewer through the current pointer position, used for scene picking.
        /// </summary>
        Ray GetRay();

        /// <summary>
        /// World position the pointer currently resolves to, typically the ray projected onto the ground.
        /// </summary>
        Vector3 GetWorldPosition();

        /// <summary>
        /// True on the frame the interact input was pressed.
        /// </summary>
        bool IsInteractPressed();
    }
}
