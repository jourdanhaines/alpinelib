using System.Threading.Tasks;
using AlpineLib.DI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlpineLib.App {
    /// <summary>
    /// Loads scenes and lets the caller await the transition.
    /// </summary>
    public interface ISceneTransitionService : IDependencyProvider {
        /// <summary>
        /// Loads a scene in single mode and completes once the load has finished.
        /// </summary>
        /// <param name="sceneName">Name of the scene to load; it must be listed in the build settings.</param>
        Task TransitionToScene(string sceneName);
    }

    /// <summary>
    /// Default <see cref="ISceneTransitionService"/>, installed on the application root and registered
    /// with the injector through its provider method.
    /// </summary>
    public class SceneTransitionService : MonoBehaviour, ISceneTransitionService {
        [Provide]
        public ISceneTransitionService ProvideSceneTransitionService() {
            return this;
        }

        public async Task TransitionToScene(string sceneName) {
            var operation = SceneManager.LoadSceneAsync(sceneName);
            while (!operation.isDone) {
                await Task.Yield();
            }
        }
    }
}
