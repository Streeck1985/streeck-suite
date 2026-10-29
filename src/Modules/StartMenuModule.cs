using System;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Módulo del menú de inicio (fase H): crea el menú y escucha la tecla Windows según los ajustes.
    /// </summary>
    public class StartMenuModule : IModule
    {
        public string Id { get { return "start_menu"; } }
        public string Name { get { return "Menú de inicio Aero"; } }
        public string Version { get { return "1.0.0"; } }
        public bool IsEnabled { get; set; }

        private StartMenuWindow _menu;
        private WinKeyHook _hook;
        private Action _settingsChanged;

        public StartMenuModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
            // Botón de inicio de la barra de Streeck
            eventBus.Subscribe(Events.StartMenuToggle, payload =>
            {
                if (_menu != null && !_menu.IsDisposed && _menu.IsHandleCreated) _menu.BeginInvoke((Action)_menu.Toggle);
            });
        }

        public void Start()
        {
            if (_menu == null || _menu.IsDisposed)
            {
                _menu = new StartMenuWindow();
                IntPtr handle = _menu.Handle; // Fuerza el manejador aunque esté oculta (CreateControl no lo crea si no es visible)
            }
            if (_hook == null)
            {
                // El gancho de teclado no debe esperar: el menú se abre después, en el hilo de la interfaz
                _hook = new WinKeyHook(() =>
                {
                    if (_menu != null && !_menu.IsDisposed && _menu.IsHandleCreated) _menu.BeginInvoke((Action)_menu.Toggle);
                    else Log.Error("[StartMenuModule] Tecla Windows pulsada pero el menú no está listo");
                });
            }
            _hook.Apply();
            _settingsChanged = () => { if (_hook != null) _hook.Apply(); };
            SuiteSettings.Changed += _settingsChanged;
        }

        public void Stop()
        {
            SuiteSettings.Changed -= _settingsChanged;
            if (_hook != null) { _hook.Dispose(); _hook = null; }
            if (_menu != null && !_menu.IsDisposed) _menu.Hide();
        }

        /// <summary>
        /// Para abrirlo desde otros sitios (botón de la futura barra de tareas, panel).
        /// </summary>
        internal void Toggle()
        {
            if (_menu != null && !_menu.IsDisposed) _menu.Toggle();
        }
    }
}
