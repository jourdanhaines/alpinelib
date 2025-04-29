using System;

namespace AlpineLib.DI {
    /// <summary>
    /// Interface that marks a class as a provider of dependencies for the dependency injection system.
    /// Implementing this interface allows the class to register methods that provide instances of dependencies
    /// to be resolved and injected by the `Injector`.
    /// </summary>
    public interface IDependencyProvider { }
     
    /// <summary>
    /// Attribute used to mark methods that provide dependencies for the injector.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ProvideAttribute : Attribute { }
}