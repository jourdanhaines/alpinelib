using System;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// One authored place a pawn may appear, in the form an inspector can edit.
    /// </summary>
    /// <remarks>
    /// The Unity-facing twin of <c>AlpineLib.Netcode.Sessions.Spawning.SpawnPoint</c>. The netcode's
    /// point is a readonly struct over a <c>System.Numerics</c> vector, which no inspector can draw, so
    /// authoring happens here and <see cref="SpawnPlacementConfig.ToPlacement"/> converts. Keeping the
    /// two apart is what lets the placement types stay engine-free and run on the dedicated server.
    /// </remarks>
    [Serializable]
    public class SpawnPointEntry {
        [Tooltip("Where the pawn stands, in world space. The height is a nominal plane; the server probes the floor around it.")]
        public Vector3 position;

        [Tooltip("Which way the pawn faces, in degrees about +Y. Zero faces along +Z.")]
        public float yawDegrees;
    }
}
