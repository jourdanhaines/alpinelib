using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlpineLib.UI {
    /// <summary>
    /// A tab strip over a <see cref="UIScreenCoordinator"/>: each tab's button shows one of the
    /// coordinator's screens, and the strip marks whichever screen the coordinator is on as active.
    /// </summary>
    /// <remarks>
    /// <see cref="ShowTab"/> and <see cref="ShowTabImmediately"/> deliberately have no overloads, so a
    /// button's persistent onClick call (an int argument) resolves to exactly one method by name.
    /// Visuals follow <see cref="UIScreenCoordinator.CurrentScreen"/> in <c>LateUpdate</c>, so a screen
    /// switched by anything else still lights the right tab.
    /// </remarks>
    public class UIScreenTabs : MonoBehaviour {
        [Tooltip("Coordinator whose screens the tabs switch between.")]
        [SerializeField] private UIScreenCoordinator coordinator;

        [Tooltip("Tabs in the same order as the coordinator's screens.")]
        [SerializeField] private List<UIScreenTab> tabs = new List<UIScreenTab>();

        [Tooltip("Ink colour of tabs that are not selected.")]
        [SerializeField] private Color idleInk = Color.white;

        [Tooltip("Ink colour of the selected tab.")]
        [SerializeField] private Color activeInk = Color.black;

        private int _renderedIndex = int.MinValue;

        /// <summary>Raised with the tab index after <see cref="ShowTab"/> or <see cref="ShowTabImmediately"/>.</summary>
        public event Action<int> TabShown;

        /// <summary>Index of the tab whose screen the coordinator shows, or −1 when none matches.</summary>
        public int ActiveIndex => coordinator != null ? IndexOf(coordinator.CurrentScreen) : -1;

        /// <summary>The tab active before the most recent switch, −1 until there has been one; the target of a "back".</summary>
        public int PreviousIndex { get; private set; } = -1;

        public IReadOnlyList<UIScreenTab> Tabs => tabs;

        public UIScreenCoordinator Coordinator => coordinator;

        private void Awake() {
            if (coordinator == null) {
                Debug.LogWarning($"{nameof(UIScreenTabs)} on '{name}' has no coordinator; tabs are inert.", this);
                return;
            }

            WarnOnOrderMismatch();
        }

        /// <summary>Fades to the tab's screen. Persistent-call target for tab buttons.</summary>
        public void ShowTab(int index) {
            SwitchTo(index, false);
        }

        /// <summary>Snaps to the tab's screen without a fade, for opening a menu on a known tab.</summary>
        public void ShowTabImmediately(int index) {
            SwitchTo(index, true);
        }

        private void LateUpdate() {
            int activeIndex = ActiveIndex;
            if (activeIndex == _renderedIndex) return;

            RefreshVisuals(activeIndex);
        }

        private void SwitchTo(int index, bool instant) {
            if (coordinator == null) return;

            if (index < 0 || index >= tabs.Count || tabs[index]?.Screen == null) {
                Debug.LogWarning($"{nameof(UIScreenTabs)} on '{name}': tab {index} is outside the {tabs.Count} tabs or has no screen.", this);
                return;
            }

            int activeIndex = ActiveIndex;
            if (activeIndex != index && activeIndex >= 0) PreviousIndex = activeIndex;

            UIScreenTab tab = tabs[index];
            coordinator.ShowScreen(tab.Screen, instant);
            if (coordinator.CurrentScreen != tab.Screen) return;

            RefreshVisuals(ActiveIndex);
            SelectFocus(tab);
            TabShown?.Invoke(index);
        }

        private void RefreshVisuals(int activeIndex) {
            _renderedIndex = activeIndex;

            for (int index = 0; index < tabs.Count; index++) {
                ApplyTabVisuals(tabs[index], index == activeIndex);
            }
        }

        private void ApplyTabVisuals(UIScreenTab tab, bool isActive) {
            if (tab == null) return;

            if (tab.ActiveMarker != null) tab.ActiveMarker.SetActive(isActive);
            if (tab.InkGraphics == null) return;

            Color ink = isActive ? activeInk : idleInk;
            foreach (Graphic graphic in tab.InkGraphics) {
                if (graphic != null) graphic.color = ink;
            }
        }

        private static void SelectFocus(UIScreenTab tab) {
            if (tab.FocusOnShow == null || EventSystem.current == null) return;

            EventSystem.current.SetSelectedGameObject(tab.FocusOnShow.gameObject);
        }

        private int IndexOf(UIScreen screen) {
            if (screen == null) return -1;

            for (int index = 0; index < tabs.Count; index++) {
                if (tabs[index] != null && tabs[index].Screen == screen) return index;
            }

            return -1;
        }

        private void WarnOnOrderMismatch() {
            IReadOnlyList<UIScreenEntry> screens = coordinator.Screens;
            bool matches = screens.Count == tabs.Count;

            for (int index = 0; matches && index < tabs.Count; index++) {
                matches = screens[index]?.Screen == tabs[index]?.Screen;
            }

            if (matches) return;

            Debug.LogWarning($"{nameof(UIScreenTabs)} on '{name}': tab order does not match the coordinator's screen order.", this);
        }
    }
}
