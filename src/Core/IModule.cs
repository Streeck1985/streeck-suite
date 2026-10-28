using System;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Interfaz que debe implementar cualquier módulo de la suite para ser cargado dinámicamente.
    /// </summary>
    public interface IModule
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        bool IsEnabled { get; set; }

        void Initialize(EventBus eventBus);
        void Start();
        void Stop();
    }
}
