using System;
using System.Collections.Generic;

namespace LunaStatusQuests.Services
{
    /// <summary>
    /// A simple service locator to manage dependencies.
    /// Acts as the central hub for registering and retrieving services throughout the mod.
    /// This pattern is used because BepInEx/Unity doesn't provide a native DI container.
    /// </summary>
    public static class ServiceContainer
    {
        // Internal storage for registered service instances, keyed by their Type (usually an interface).
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>
        /// Registers a service instance under a specific type T.
        /// Usually called during the mod's Awake method (Plugin class).
        /// </summary>
        /// <typeparam name="T">The interface or base type to register under (e.g., IQuestService).</typeparam>
        /// <param name="service">The concrete implementation instance.</param>
        public static void Register<T>(T service)
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                _services[type] = service;
            }
            else
            {
                _services.Add(type, service);
            }
        }

        /// <summary>
        /// Resolves and returns a registered service of type T.
        /// Used by patches and other components to access shared logic.
        /// </summary>
        /// <typeparam name="T">The type of service to retrieve.</typeparam>
        /// <returns>The registered service instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the requested service was never registered.</exception>
        public static T Resolve<T>()
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
            {
                return (T)service;
            }

            throw new InvalidOperationException(
                $"Service of type {type.Name} is not registered. Ensure it is registered in the Plugin's Awake method."
            );
        }

        /// <summary>
        /// Clears all registered services.
        /// Typically used during cleanup or if the mod needs to be re-initialized.
        /// </summary>
        public static void Clear()
        {
            _services.Clear();
        }
    }
}
