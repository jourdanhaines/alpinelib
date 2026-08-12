using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.UI {
    /// <summary>
    /// Owns an ordered set of <see cref="UIScreen"/>s and drives which one is up: seeds the first
    /// screen on spawn, switches screens on request, and re-seeds any nested coordinators inside a
    /// screen it shows. Screen-specific behaviour stays on each screen's own components; this class
    /// owns only construction and flow.
    /// </summary>
    /// <remarks>
    /// Switches are sequential, never a crossfade: the outgoing screen fades fully out, then the
    /// incoming screen fades in. Two half-transparent menus stacked on top of each other read as a
    /// glitch, and a strict out-then-in keeps every frame looking like an authored state. All fade
    /// timing is set from here (<see cref="UIScreenEntry"/>) because pacing is a menu-wide style
    /// decision — screens are deliberately not trusted to pick their own.
    ///
    /// Nesting works by hierarchy alone: a screen's subtree may contain its own coordinator, and
    /// showing that screen resets the child to its first screen instantly, so a menu is always
    /// re-entered at its start rather than wherever the player last left it. Only directly owned
    /// children are reset — a child's own children are the child's business, handled by the same
    /// rule one level down.
    ///
    /// Seeding runs in <c>Awake</c>, which is safe regardless of component order because
    /// <see cref="UIScreen"/> resolves its own state on first contact. This also means any preview
    /// alphas left behind by the editor tooling are normalised the moment the scene enters play.
    /// </remarks>
    public class UIScreenCoordinator : MonoBehaviour {
        [Tooltip("Seconds a full fade takes for any screen without its own override. The menu's single pacing knob.")]
        [SerializeField] private float fadeDuration = 0.15f;

        [Tooltip("Show the first listed screen, and hide the rest, as soon as this object enters the scene.")]
        [SerializeField] private bool showOnSpawn = true;

        [Tooltip("Screens this coordinator owns, in flow order. The first entry is the screen shown on spawn.")]
        [SerializeField] private List<UIScreenEntry> screens = new List<UIScreenEntry>();

        /// <summary>
        /// The screen the coordinator is showing or transitioning towards, null after
        /// <see cref="HideAll"/>. Tracks intent, matching <see cref="UIScreen.IsVisible"/>.
        /// </summary>
        public UIScreen CurrentScreen { get; private set; }

        public IReadOnlyList<UIScreenEntry> Screens => screens;

        private Coroutine _transition;

        private void Awake() {
            if (screens.Count == 0) {
                Debug.LogWarning("UIScreenCoordinator::Awake->No screens listed; coordinator is inert.", this);
                return;
            }

            if (!showOnSpawn) return;

            ResetToFirst(true);
        }

        /// <summary>Shows the screen at <paramref name="index"/> in the listed order.</summary>
        public void ShowScreen(int index, bool instant = false) {
            if (index < 0 || index >= screens.Count) {
                Debug.LogWarning($"UIScreenCoordinator::ShowScreen->Index {index} is outside the {screens.Count} listed screens.", this);
                return;
            }

            ShowScreen(screens[index].Screen, instant);
        }

        /// <summary>
        /// Fades out whatever is currently up, then fades <paramref name="screen"/> in. The screen
        /// must be one of the listed entries; anything else is refused with a warning so
        /// <see cref="CurrentScreen"/> can never point at a screen this coordinator does not own.
        /// </summary>
        public void ShowScreen(UIScreen screen, bool instant = false) {
            UIScreenEntry incomingEntry = FindEntry(screen);
            if (incomingEntry == null) {
                Debug.LogWarning("UIScreenCoordinator::ShowScreen->Screen is null or not listed on this coordinator.", this);
                return;
            }

            UIScreen outgoing = CurrentScreen;
            CurrentScreen = screen;
            CancelTransition(screen);

            if (instant) {
                CompleteSwitchTo(incomingEntry, true);
                return;
            }

            _transition = StartCoroutine(TransitionRoutine(outgoing, incomingEntry));
        }

        /// <summary>
        /// Returns the flow to its first screen — the spawn seed, and the natural target for a
        /// top-level "back".
        /// </summary>
        public void ResetToFirst(bool instant = false) {
            ShowScreen(0, instant);
        }

        /// <summary>
        /// Takes every listed screen down. Called instant before a scene transition so no screen is
        /// caught mid-fade by the transition's own screen grab.
        /// </summary>
        public void HideAll(bool instant = false) {
            CancelTransition(null);
            CurrentScreen = null;

            foreach (UIScreenEntry entry in screens) {
                if (entry?.Screen == null) continue;

                entry.Screen.FadeDuration = entry.ResolveFadeDuration(fadeDuration);
                entry.Screen.Hide(instant);
            }
        }

        /// <summary>
        /// The sequential switch: outgoing fully out, then incoming in, then nested re-seed. The
        /// incoming screen's nested coordinators are reset before it starts fading in, so the screen
        /// arrives already showing its first sub-screen instead of rearranging in front of the player.
        /// </summary>
        private IEnumerator TransitionRoutine(UIScreen outgoing, UIScreenEntry incomingEntry) {
            if (outgoing != null && outgoing != incomingEntry.Screen) {
                UIScreenEntry outgoingEntry = FindEntry(outgoing);
                outgoing.FadeDuration = outgoingEntry != null ? outgoingEntry.ResolveFadeDuration(fadeDuration) : fadeDuration;
                outgoing.Hide();

                while (outgoing.IsFading) yield return null;
            }

            CompleteSwitchTo(incomingEntry, false);

            while (incomingEntry.Screen.IsFading) yield return null;

            _transition = null;
        }

        /// <summary>Re-seeds the incoming screen's nested coordinators and brings it up.</summary>
        private void CompleteSwitchTo(UIScreenEntry incomingEntry, bool instant) {
            ReseedNestedCoordinators(incomingEntry.Screen);
            incomingEntry.Screen.FadeDuration = incomingEntry.ResolveFadeDuration(fadeDuration);
            incomingEntry.Screen.Show(instant);
        }

        /// <summary>
        /// Stops any switch in flight and snaps every listed screen except
        /// <paramref name="protectedScreen"/> to hidden, so an interrupted sequence cannot leave a
        /// half-faded straggler behind.
        /// </summary>
        private void CancelTransition(UIScreen protectedScreen) {
            if (_transition == null) return;

            StopCoroutine(_transition);
            _transition = null;

            foreach (UIScreenEntry entry in screens) {
                if (entry?.Screen == null || entry.Screen == protectedScreen) continue;

                entry.Screen.Hide(true);
            }
        }

        /// <summary>
        /// Resets each coordinator directly owned by <paramref name="shownScreen"/> to its first
        /// screen. Grandchildren are skipped — the child's own reset repeats this rule one level
        /// down, so the whole subtree lands on its start state without anything being seeded twice.
        /// </summary>
        private void ReseedNestedCoordinators(UIScreen shownScreen) {
            UIScreenCoordinator[] descendants = shownScreen.GetComponentsInChildren<UIScreenCoordinator>(true);

            foreach (UIScreenCoordinator candidate in descendants) {
                if (candidate == this) continue;
                if (!IsDirectlyOwned(candidate.transform, shownScreen.transform)) continue;

                candidate.ResetToFirst(true);
            }
        }

        /// <summary>
        /// True when no other coordinator sits on the transform walk between
        /// <paramref name="candidate"/>'s parent and <paramref name="screenRoot"/>. A coordinator on
        /// the screen's own GameObject is direct by definition.
        /// </summary>
        private static bool IsDirectlyOwned(Transform candidate, Transform screenRoot) {
            if (candidate == screenRoot) return true;

            for (Transform ancestor = candidate.parent; ancestor != null && ancestor != screenRoot; ancestor = ancestor.parent) {
                if (ancestor.GetComponent<UIScreenCoordinator>() != null) return false;
            }

            return true;
        }

        private UIScreenEntry FindEntry(UIScreen screen) {
            if (screen == null) return null;

            foreach (UIScreenEntry entry in screens) {
                if (entry != null && entry.Screen == screen) return entry;
            }

            return null;
        }
    }
}
