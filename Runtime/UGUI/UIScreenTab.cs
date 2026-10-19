using System;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineLib.UI {
    /// <summary>
    /// One tab in a <see cref="UIScreenTabs"/> strip: the button that selects it, the screen it shows,
    /// and the graphics that mark it active.
    /// </summary>
    [Serializable]
    public class UIScreenTab {
        [Tooltip("Button that selects this tab. Wire its onClick to UIScreenTabs.ShowTab with this tab's index.")]
        [SerializeField] private Button button;

        [Tooltip("Screen shown when this tab is selected. Must be listed on the tabs' coordinator.")]
        [SerializeField] private UIScreen screen;

        [Tooltip("Optional object active only while this tab is selected, such as a fill behind the label.")]
        [SerializeField] private GameObject activeMarker;

        [Tooltip("Graphics tinted with the active or idle ink colour.")]
        [SerializeField] private Graphic[] inkGraphics = Array.Empty<Graphic>();

        [Tooltip("Optional control selected when this tab is shown, for keyboard and gamepad focus.")]
        [SerializeField] private Selectable focusOnShow;

        public Button Button => button;

        public UIScreen Screen => screen;

        public GameObject ActiveMarker => activeMarker;

        public Graphic[] InkGraphics => inkGraphics;

        public Selectable FocusOnShow => focusOnShow;
    }
}
