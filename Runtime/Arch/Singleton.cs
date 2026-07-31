using UnityEngine;

namespace AlpineLib.Arch {
    public class Singleton<T> : MonoBehaviour where T : Component {
        protected static T instance;

        // Guards against teardown-order races: an OnDestroy that touches Instance while the
        // application is quitting must not spawn a replacement singleton into the dying scene.
        private static bool isApplicationQuitting;

        public static bool HasInstance => instance != null;
        public static T TryGetInstance() => HasInstance ? instance : null;
        public static T Current => instance;

        public static T Instance {
            get {
                if (instance == null) {
                    if (isApplicationQuitting) {
                        return null;
                    }

                    instance = FindFirstObjectByType<T>();
                    if (instance == null) {
                        GameObject obj = new GameObject();
                        obj.name = typeof(T).Name + " (Singleton)";
                        instance = obj.AddComponent<T>();
                    }
                }

                return instance;
            }
        }

        protected virtual void Awake() => InitializeSingleton();

        protected virtual void InitializeSingleton() {
            if (!Application.isPlaying) {
                return;
            }

            // Reset here rather than via RuntimeInitializeOnLoadMethod: Unity never invokes
            // those on generic types, and with domain reload disabled the flag would otherwise
            // stay true into the next play session.
            isApplicationQuitting = false;
            instance = this as T;
        }

        protected virtual void OnApplicationQuit() {
            isApplicationQuitting = true;
        }
    }
}