using UnityEngine;

namespace AlpineLib.App {
    /// <summary>
    /// Persistent host for application-wide services, created by <see cref="AppBootstrapper"/> before the
    /// first scene loads and kept alive for the lifetime of the application.
    /// </summary>
    /// <remarks>
    /// The base root installs nothing: a game derives from it and overrides <see cref="InstallServices"/>
    /// to add the service components it needs. Services live as components on this object so the
    /// injector discovers and registers them like any other provider in the scene.
    /// </remarks>
    public class AppRoot : MonoBehaviour {
        /// <summary>
        /// Brings the root up. Called once by <see cref="AppBootstrapper"/> immediately after the root is
        /// created, before any scene has loaded.
        /// </summary>
        public void Init() {
            InstallServices();
        }

        /// <summary>
        /// Adds the application-wide service components to this object. Installs nothing by default.
        /// </summary>
        protected virtual void InstallServices() { }

        private void Awake() {
            DontDestroyOnLoad(this);
        }
    }
}
