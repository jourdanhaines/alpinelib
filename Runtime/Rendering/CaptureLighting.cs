using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// The light a capture is drawn under: a flat ambient colour and an optional directional key.
    /// <see cref="Literal"/> shows surfaces at their own colour (map tiles); <see cref="Portrait"/> adds
    /// a soft key for shape (avatar portraits).
    /// </summary>
    public readonly struct CaptureLighting {
        /// <summary>Builds a lighting setup; a zero <paramref name="keyIntensity"/> means no key light.</summary>
        public CaptureLighting(Color ambient, Color keyColour, float keyIntensity, Vector3 keyEulerDegrees) {
            Ambient = ambient;
            KeyColour = keyColour;
            KeyIntensity = keyIntensity;
            KeyEulerDegrees = keyEulerDegrees;
        }

        /// <summary>Flat white ambient and no key: albedo as authored, no shading.</summary>
        public static CaptureLighting Literal => new CaptureLighting(Color.white, Color.white, 0f, Vector3.zero);

        /// <summary>Soft ambient plus a key from above-front-left, in the camera's frame.</summary>
        public static CaptureLighting Portrait => new CaptureLighting(new Color(0.62f, 0.62f, 0.62f), Color.white, 0.75f, new Vector3(35f, -30f, 0f));

        /// <summary>Flat ambient colour.</summary>
        public Color Ambient { get; }

        /// <summary>Key light colour.</summary>
        public Color KeyColour { get; }

        /// <summary>Key light intensity; zero for no key.</summary>
        public float KeyIntensity { get; }

        /// <summary>Key light rotation relative to the capture camera, in degrees.</summary>
        public Vector3 KeyEulerDegrees { get; }

        /// <summary>True when the setup adds a directional key light.</summary>
        public bool HasKey => KeyIntensity > 0f;
    }
}
