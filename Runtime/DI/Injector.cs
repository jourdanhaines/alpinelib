using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AlpineLib.Arch;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlpineLib.DI {
    ///<summary>
    /// Attribute used to mark fields or methods for dependency injection.
    /// </summary> 
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public sealed class InjectAttribute : Attribute { }
    
    /// <summary>
    /// Dependency injection system that automatically resolves and injects dependencies into objects.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class Injector : Singleton<Injector> {
        /// <summary>
        /// Binding flags used to search for fields and methods in classes.
        /// </summary>
        private const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        
        /// <summary>
        /// Registry of resolved dependencies, mapping types to their instances.
        /// </summary>
        private readonly Dictionary<Type, object> _registry = new Dictionary<Type, object>();

        /// <summary>
        /// Called when the singleton instance is initialized. Registers providers and injects dependencies.
        /// </summary>
        protected override void Awake() {
            if (HasInstance && !ReferenceEquals(Current, this)) {
                // Installing the injector is racy by nature: it is created from a BeforeSceneLoad hook and
                // by whoever touches Instance first. A second registry would hand out stale instances, so
                // the loser of the race removes itself.
                Destroy(gameObject);
                return;
            }

            base.Awake();

            DontDestroyOnLoad(this);

            SceneManager.sceneLoaded += OnSceneLoaded;
            
            PerformInjection();
        }

        private void OnDestroy() {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            PerformInjection();
        }

        private void PerformInjection() {
            RegisterNewProviders();
            InjectAllSceneObjects();
        }

        private void RegisterNewProviders() {
            var providers = FindMonoBehaviours().OfType<IDependencyProvider>();

            foreach (var provider in providers) {
                if (_registry.Values.Contains(provider)) continue;
                
                RegisterProvider(provider);
            }
        }

        private void InjectAllSceneObjects() {
            var injectables = FindMonoBehaviours().Where(IsInjectable);
            foreach (var injectable in injectables) {
                Inject(injectable);
            }
        }
        
        public void InjectDependency<T>(T instance) {
            if (instance == null) {
                Debug.LogWarning($"[Injector] Attempted to inject null instance of {typeof(T).Name}");
                return;
            }
            
            if (instance is MonoBehaviour monoBehaviour) {
                if (!IsInjectable(monoBehaviour)) {
                    Debug.LogWarning($"[Injector] {monoBehaviour.gameObject.name} is not injectable.");
                    return;
                }
            }

            Inject(instance);
        }

        /// <summary>
        /// Injects dependencies into the specified object.
        /// </summary>
        /// <param name="instance">The object to inject dependencies into.</param>
        private void Inject(object instance) {
            var type = instance.GetType();
            var injectableFields = type.GetFields(bindingFlags)
                .Where(field => Attribute.IsDefined(field, typeof(InjectAttribute)));
            
            foreach (var field in injectableFields) {
                var fieldType = field.FieldType;
                var resolvedInstance = Resolve(fieldType);
                
                if (resolvedInstance == null) {
                    // The field type was not registered
                    throw new Exception($"Failed to inject {fieldType.Name} into {type.Name}");
                }
                
                field.SetValue(instance, resolvedInstance);
                Debug.Log($"Field injected {fieldType.Name} into {type.Name}");
            }
            
            var injectableMethods = type.GetMethods(bindingFlags)
                .Where(method => Attribute.IsDefined(method, typeof(InjectAttribute)));

            foreach (var method in injectableMethods) {
                var requiredParameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                var resolvedInstances = requiredParameters.Select(Resolve).ToArray();
                
                if (resolvedInstances.Any(instance => instance == null)) {
                    // One or more parameters could not be resolved
                    throw new Exception($"Failed to inject {type.Name}.{method.Name}");
                }

                method.Invoke(instance, resolvedInstances);
                Debug.Log($"Method injected {type.Name}.{method.Name}");
            }
        }

        /// <summary>
        /// Resolves an instance of the specified type from the registry.
        /// </summary>
        /// <param name="type">The type to resolve.</param>
        /// <returns>The resolved instance, or null if not found.</returns>
        private object Resolve(Type type) {
            _registry.TryGetValue(type, out var resolvedInstance);
            return resolvedInstance;
        }

        /// <summary>
        /// Registers a dependency provider and its provided dependencies.
        /// </summary>
        /// <param name="provider">The provider to register.</param>
        public void RegisterProvider(IDependencyProvider provider) {
            var methods = provider.GetType().GetMethods(bindingFlags);

            foreach (var method in methods) {
                if (!Attribute.IsDefined(method, typeof(ProvideAttribute))) continue;

                var returnType = method.ReturnType;
                
                if (_registry.ContainsKey(returnType)) {
                    Debug.LogWarning($"Provider for {returnType.Name} already registered, skipping.");
                    continue; // avoid duplicate registrations
                }
                
                var providedInstance = method.Invoke(provider, null);
                
                if (providedInstance == null) {
                    // The provider did not supply what we expected
                    throw new Exception($"Provider {provider.GetType().Name} returned null for {returnType.Name}");
                }
                
                _registry.Add(returnType, providedInstance);
                
                Debug.Log($"Provider registered {returnType.Name} from {provider.GetType().Name}");
            }
        }

        /// <summary>
        /// Unregisters a dependency provider, dropping the dependencies it supplied from the registry.
        /// </summary>
        /// <remarks>
        /// Only entries the given provider supplied are removed, so a replacement that registered ahead of
        /// this call survives. Call this before destroying a provider: a registry entry pointing at a
        /// destroyed instance is injected into every object loaded afterwards.
        /// </remarks>
        /// <param name="provider">The provider to unregister.</param>
        public void UnregisterProvider(IDependencyProvider provider) {
            var methods = provider.GetType().GetMethods(bindingFlags);

            foreach (var method in methods) {
                if (!Attribute.IsDefined(method, typeof(ProvideAttribute))) continue;

                var returnType = method.ReturnType;

                if (!_registry.TryGetValue(returnType, out var registeredInstance)) continue;
                if (!ReferenceEquals(registeredInstance, provider)) continue;

                _registry.Remove(returnType);

                Debug.Log($"Provider unregistered {returnType.Name} from {provider.GetType().Name}");
            }
        }

        /// <summary>
        /// Determines if a MonoBehaviour object has members marked with the Inject attribute.
        /// </summary>
        /// <param name="obj">The MonoBehaviour object to check.</param>
        /// <returns>True if the object is injectable, otherwise false.</returns>
        private static bool IsInjectable(MonoBehaviour obj) {
            var members = obj.GetType().GetMembers(bindingFlags);
            return members.Any(member => Attribute.IsDefined(member, typeof(InjectAttribute)));
        }

        /// <summary>
        /// Finds all MonoBehaviour instances in the scene.
        /// </summary>
        /// <returns>An array of MonoBehaviour instances.</returns>
        private static MonoBehaviour[] FindMonoBehaviours() {
            return FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.InstanceID);
        }
    }
}