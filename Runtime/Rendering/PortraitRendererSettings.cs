using System;
using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>Target size, background, lighting and staging position shared by every portrait one renderer draws.</summary>
    [Serializable]
    public class PortraitRendererSettings {
        [Tooltip("Square render target side, pixels.")]
        [SerializeField] private int size = 256;

        [Tooltip("Clear colour behind the subject.")]
        [SerializeField] private Color background = Color.white;

        [Tooltip("MSAA samples on the render target.")]
        [SerializeField] private int msaaSamples = 4;

        [Tooltip("World position subjects are staged at, far from anything else.")]
        [SerializeField] private Vector3 stagePosition = new Vector3(0f, -6000f, 0f);

        /// <summary>Square render target side, pixels.</summary>
        public int Size { get => size; set => size = value; }

        /// <summary>Clear colour behind the subject.</summary>
        public Color Background { get => background; set => background = value; }

        /// <summary>MSAA samples on the render target.</summary>
        public int MsaaSamples { get => msaaSamples; set => msaaSamples = value; }

        /// <summary>World position subjects are staged at.</summary>
        public Vector3 StagePosition { get => stagePosition; set => stagePosition = value; }

        /// <summary>Lighting every portrait is drawn under.</summary>
        public CaptureLighting Lighting { get; set; } = CaptureLighting.Portrait;
    }
}
