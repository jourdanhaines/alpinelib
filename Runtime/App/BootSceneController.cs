using AlpineLib.DI;
using UnityEngine;

namespace AlpineLib.App {
    /// <summary>
    /// Leaves the boot scene as soon as it starts, handing over to the first real scene of the game.
    /// </summary>
    /// <remarks>
    /// The boot scene exists so the application root is created from code before any gameplay content
    /// loads; this controller keeps it empty and momentary.
    /// </remarks>
    public class BootSceneController : MonoBehaviour {
        [Inject] private ISceneTransitionService _sceneTransitionService;

        [SerializeField] private string nextSceneName;

        private async void Start() {
            Injector.Instance.InjectDependency(this);

            await _sceneTransitionService.TransitionToScene(nextSceneName);
        }
    }
}
