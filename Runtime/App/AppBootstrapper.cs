using AlpineLib.DI;
using UnityEngine;

namespace AlpineLib.App {
    /// <summary>
    /// Code-driven application entry point: creates the persistent <see cref="AppRoot"/> and installs its
    /// services.
    /// </summary>
    /// <remarks>
    /// A game calls <see cref="Boot{TRoot}"/> from its own
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/> hook with
    /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>, so the root and its services exist before
    /// the first scene wakes up.
    /// </remarks>
    public static class AppBootstrapper {
        /// <summary>
        /// Creates the application root of the given type and initialises it.
        /// </summary>
        /// <typeparam name="TRoot">Concrete root type whose <c>InstallServices</c> override installs the
        /// application-wide services.</typeparam>
        /// <returns>The created root, which survives scene loads.</returns>
        public static TRoot Boot<TRoot>() where TRoot : AppRoot {
            // The injector is also installed from a BeforeSceneLoad hook and Unity leaves the order of
            // those hooks undefined, so it is forced into existence first: services installed below may
            // register themselves with it while they wake up.
            _ = Injector.Instance;

            var rootObject = new GameObject("AppRoot");
            var root = rootObject.AddComponent<TRoot>();
            root.Init();

            return root;
        }
    }
}
