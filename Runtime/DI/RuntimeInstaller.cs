using UnityEngine;

namespace AlpineLib.DI {
    [DefaultExecutionOrder(-10000)]
    internal sealed class RuntimeInstaller : MonoBehaviour {
        private static bool _created;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() {
            if (_created) return;
            _created = true;

            var go = new GameObject("AlpineLib.Injector");
            DontDestroyOnLoad(go);

            go.AddComponent<Injector>();
        }
    }
}