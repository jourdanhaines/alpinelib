using System.Threading.Tasks;
using AlpineLib.App;
using AlpineLib.DI;
using AlpineLib.Netcode.Sessions;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// Turns what the session says into scene loads: a match announcement becomes the match scene and a
    /// ready report, a return to the lobby becomes the lobby scene, and a session that ends drops the
    /// player back into the offline fallback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept apart from <see cref="SessionService"/> because they answer to different owners. The service
    /// owns the connection and would work identically in a headless test with no scenes at all; this
    /// owns the game's scene vocabulary and does nothing but translate. A game that manages its own
    /// scene transitions simply does not install it.
    /// </para>
    /// <para>
    /// The ready report is what makes the barrier work: every participant loads at its own speed, tells
    /// the server when it has arrived, and the match starts only once the slowest has — or once the
    /// server's patience runs out and its late-load policy decides what to do with the straggler. So the
    /// report is sent strictly after the load has completed, never optimistically alongside it.
    /// </para>
    /// </remarks>
    public class SessionSceneFlow : MonoBehaviour {
        [Inject] private ISceneTransitionService _sceneTransitionService;

        private ISessionService _sessionService;

        /// <remarks>
        /// The session service is resolved rather than injected so this component survives a scene with
        /// no networking installed: without a session there is nothing to translate and it sits idle.
        /// </remarks>
        private void Start() {
            if (!Injector.HasInstance) return;

            Injector.Instance.InjectDependency(this);

            if (!Injector.Instance.TryResolve(out _sessionService)) {
                Debug.LogWarning("SessionSceneFlow::Start->No session service; scene flow is inert.");
                return;
            }

            _sessionService.OnMatchLoading += HandleMatchLoading;
            _sessionService.OnReturnedToLobby += HandleReturnedToLobby;
            _sessionService.OnSessionEnded += HandleSessionEnded;
        }

        private void OnDestroy() {
            if (_sessionService == null) return;

            _sessionService.OnMatchLoading -= HandleMatchLoading;
            _sessionService.OnReturnedToLobby -= HandleReturnedToLobby;
            _sessionService.OnSessionEnded -= HandleSessionEnded;
        }

        /// <remarks>
        /// The load is started and deliberately not awaited here: this is an event handler, and an
        /// <c>async void</c> one would swallow a failed load into an unobserved exception. The task is
        /// owned by <see cref="LoadMatchSceneAsync"/>, which reports its own problems.
        /// </remarks>
        private void HandleMatchLoading(MatchContextData match) {
            _ = LoadMatchSceneAsync(match);
        }

        private void HandleReturnedToLobby() {
            _ = LoadSceneAsync(ResolveLobbySceneName());
        }

        private void HandleSessionEnded(SessionEndReason reason, string message) {
            _ = LoadSceneAsync(ResolveFallbackSceneName());
        }

        /// <summary>
        /// Loads a match's scene and, once it is actually loaded, tells the server this client has
        /// arrived.
        /// </summary>
        private async Task LoadMatchSceneAsync(MatchContextData match) {
            if (match == null) {
                Debug.LogError("SessionSceneFlow::LoadMatchSceneAsync->Match announced with no context.");
                return;
            }

            if (!await LoadSceneAsync(match.SceneName)) return;

            _sessionService.NotifyClientReady(match.MatchSequence);
        }

        /// <summary>Loads a scene by name, reporting rather than throwing when it is not configured.</summary>
        /// <returns>True once the scene has finished loading.</returns>
        private async Task<bool> LoadSceneAsync(string sceneName) {
            if (string.IsNullOrEmpty(sceneName)) {
                Debug.LogError("SessionSceneFlow::LoadSceneAsync->No scene name configured for this transition.");
                return false;
            }

            if (_sceneTransitionService == null) {
                Debug.LogError("SessionSceneFlow::LoadSceneAsync->No scene transition service installed.");
                return false;
            }

            await _sceneTransitionService.TransitionToScene(sceneName);
            return true;
        }

        private string ResolveLobbySceneName() {
            SessionConfig config = _sessionService?.Config;

            if (config == null || config.lobby == null) return string.Empty;

            return config.lobby.lobbySceneName;
        }

        private string ResolveFallbackSceneName() {
            SessionConfig config = _sessionService?.Config;

            if (config == null) return string.Empty;

            return config.offlineFallbackSceneName;
        }
    }
}
