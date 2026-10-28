using System;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Módulo Taskbar Island: Controla la división de la barra de tareas en 3 cápsulas flotantes y el espacio entre ellas.
    /// </summary>
    public class TaskbarIslandModule : IModule
    {
        public string Id { get { return "taskbar_islands"; } }
        public string Name { get { return "Barra de Tareas en 3 Islas Flotantes"; } }
        public string Version { get { return "1.0.0"; } }
        public bool IsEnabled { get; set; }

        private EventBus _eventBus;
        private string _currentMode = "split"; // "split", "dock", "edges"
        private int _islandGap = 14;

        public TaskbarIslandModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
            _eventBus = eventBus;
            _eventBus.Subscribe(Events.TaskbarSetMode, OnSetMode);
            _eventBus.Subscribe(Events.TaskbarSetGap, OnSetGap);
        }

        public void Start()
        {
            Log.Info("[TaskbarIslandModule] Modo activo: {0}, Espaciado: {1}px", _currentMode, _islandGap);
        }

        public void Stop()
        {
            Log.Info("[TaskbarIslandModule] Detenido");
        }

        private void OnSetMode(object payload)
        {
            string mode = payload as string ?? "split";
            _currentMode = mode;
            _eventBus.Publish(Events.NotchShowMessage, string.Format("🪟 Barra de tareas: Modo {0}", mode));
            Log.Info("[TaskbarIslandModule] Cambiando a modo: {0}", mode);
        }

        private void OnSetGap(object payload)
        {
            if (payload is int)
            {
                _islandGap = (int)payload;
                Log.Info("[TaskbarIslandModule] Nuevo espaciado: {0}px", _islandGap);
            }
        }
    }
}
