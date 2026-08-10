using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.UI {
    /// <summary>
    /// Last-in-first-out stack of <see cref="UIScreen"/>s, showing and hiding them as they are pushed
    /// and popped. One lives per scene and owns the navigation order for that scene's menus, so screens
    /// themselves never need to know what sits above or below them.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="MonoBehaviour"/> rather than a service: screen stacks are scene-scoped by
    /// nature — the menus of a loaded scene die with it — and a global one would outlive its screens and
    /// hold references to destroyed objects across a transition.
    ///
    /// A <see cref="List{T}"/> backs the stack instead of <see cref="Stack{T}"/> because
    /// <see cref="Top"/> must be readable without popping and pushes need to check for an entry that is
    /// already in the stack, neither of which a stack exposes.
    /// </remarks>
    public class UIScreenStack : MonoBehaviour {
        [Tooltip("Hide the screen underneath when a new one is pushed, and show it again when that one pops. Leave off for overlays that should sit on top of what they cover.")]
        [SerializeField] private bool hideBeneath;

        /// <summary>
        /// Screen currently on top of the stack, or null while the stack is empty.
        /// </summary>
        public UIScreen Top => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;

        /// <summary>
        /// Number of screens currently on the stack.
        /// </summary>
        public int Count => _screens.Count;

        private readonly List<UIScreen> _screens = new List<UIScreen>();

        /// <summary>
        /// Pushes a screen onto the stack and shows it, hiding the one it covers when
        /// <c>hideBeneath</c> is set.
        /// </summary>
        /// <remarks>
        /// Pushing a screen that is already on the stack is ignored rather than duplicated: a double
        /// push — two input sources reacting to the same key, say — would otherwise leave a second entry
        /// behind that the first pop hides while the duplicate keeps the screen listed as open.
        /// </remarks>
        public void Push(UIScreen screen) {
            if (screen == null) return;
            if (_screens.Contains(screen)) return;

            if (hideBeneath && Top != null) {
                Top.Hide();
            }

            _screens.Add(screen);
            screen.Show();
        }

        /// <summary>
        /// Pops the top screen and hides it, revealing the one beneath when <c>hideBeneath</c> is set.
        /// Popping an empty stack does nothing.
        /// </summary>
        public void Pop() {
            if (_screens.Count == 0) return;

            UIScreen popped = Top;
            _screens.RemoveAt(_screens.Count - 1);
            popped.Hide();

            if (!hideBeneath) return;
            if (Top == null) return;

            Top.Show();
        }
    }
}
