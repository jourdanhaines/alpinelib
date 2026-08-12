using System;
using UnityEngine;

namespace AlpineLib.UI {
    /// <summary>
    /// One screen owned by a <see cref="UIScreenCoordinator"/>: the screen itself plus how it takes
    /// part in the coordinator's fade styling.
    /// </summary>
    /// <remarks>
    /// Fade timing lives here, on the coordinator's side of the fence, rather than on the screen:
    /// the coordinator owns the menu's overall style and rhythm, while each screen owns only its own
    /// layout and controls. A negative override means "inherit the coordinator's duration", which is
    /// the normal case; a non-negative value pins this one screen faster or slower than the rest.
    /// </remarks>
    [Serializable]
    public class UIScreenEntry {
        [Tooltip("Screen this entry drives.")]
        [SerializeField] private UIScreen screen;

        [Tooltip("Seconds this screen's fades take. Negative inherits the coordinator's fade duration; zero makes this screen switch instantly.")]
        [SerializeField] private float fadeDurationOverride = -1f;

        public UIScreen Screen => screen;

        /// <summary>The fade duration this entry's screen should use, given the coordinator's default.</summary>
        public float ResolveFadeDuration(float coordinatorDefault) {
            return fadeDurationOverride < 0f ? coordinatorDefault : fadeDurationOverride;
        }
    }
}
