using AlpineLib.DI;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>The session's global wind, sampled once per frame from the shared clock.</summary>
    public interface IWindService : IDependencyProvider {
        /// <summary>The wind function, or null before it is configured.</summary>
        WindField Field { get; }

        /// <summary>Session seconds of the current sample.</summary>
        double Time { get; }

        /// <summary>Wind velocity now, m/s, in Unity axes (y 0).</summary>
        Vector3 Velocity { get; }

        /// <summary>How far the air has moved since the session began, metres (x East, z North).</summary>
        WindVector Displacement { get; }
    }
}
