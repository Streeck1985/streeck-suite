using System;
using System.Collections.Generic;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Coordinador dinámico del ciclo de vida de los módulos.
    /// Permite añadir o retirar módulos en caliente sin reiniciar el sistema.
    /// </summary>
    public class ModuleManager
    {
        private static readonly Lazy<ModuleManager> _instance = new Lazy<ModuleManager>(CreateInstance);

        private static ModuleManager CreateInstance()
        {
            return new ModuleManager();
        }

        public static ModuleManager Instance
        {
            get { return _instance.Value; }
        }

        private readonly Dictionary<string, IModule> _modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        private readonly EventBus _eventBus;

        public ModuleManager()
        {
            _eventBus = EventBus.Instance;
        }

        public void Register(IModule module)
        {
            if (module == null) throw new ArgumentNullException("module");

            if (!_modules.ContainsKey(module.Id))
            {
                try
                {
                    module.Initialize(_eventBus);
                }
                catch (Exception ex)
                {
                    // Un módulo que no se puede inicializar se descarta sin tumbar al resto
                    Log.Error("[ModuleManager] Error al inicializar {0}: {1}", module.Name, ex);
                    return;
                }
                _modules[module.Id] = module;
                Log.Info("[ModuleManager] Módulo registrado: {0} v{1}", module.Name, module.Version);
            }
        }

        public void StartAll()
        {
            foreach (KeyValuePair<string, IModule> kvp in _modules)
            {
                IModule module = kvp.Value;
                if (module.IsEnabled)
                {
                    try
                    {
                        module.Start();
                        Log.Info("[ModuleManager] Módulo iniciado: {0}", module.Name);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[ModuleManager] Error al iniciar {0}: {1}", module.Name, ex);
                    }
                }
            }
            _eventBus.Publish(Events.AllModulesStarted);
        }

        public void StopAll()
        {
            foreach (KeyValuePair<string, IModule> kvp in _modules)
            {
                IModule module = kvp.Value;
                try
                {
                    module.Stop();
                    Log.Info("[ModuleManager] Módulo detenido: {0}", module.Name);
                }
                catch (Exception ex)
                {
                    Log.Error("[ModuleManager] Error al detener {0}: {1}", module.Name, ex);
                }
            }
            _eventBus.Publish(Events.AllModulesStopped);
        }

        public IModule GetModule(string id)
        {
            IModule mod;
            if (_modules.TryGetValue(id, out mod))
            {
                return mod;
            }
            return null;
        }

        public IEnumerable<IModule> GetAllModules()
        {
            return _modules.Values;
        }
    }
}
