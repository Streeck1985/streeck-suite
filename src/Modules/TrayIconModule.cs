using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Módulo Tray Icon: Icono interactivo en la bandeja del sistema de Windows con menú contextual rápido.
    /// </summary>
    public class TrayIconModule : IModule
    {
        public string Id { get { return "tray_icon"; } }
        public string Name { get { return "Icono de Bandeja del Sistema"; } }
        public string Version { get { return "1.0.0"; } }
        public bool IsEnabled { get; set; }

        private EventBus _eventBus;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;

        public TrayIconModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Start()
        {
            // Se crea aquí (y no en Initialize) para que Stop() pueda liberarlo y un Start() posterior lo recree
            if (_notifyIcon != null) return;

            CreateTray();
            _notifyIcon.Visible = true;
        }

        public void Stop()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
                _contextMenu = null;
            }
        }

        private void CreateTray()
        {
            // Solo opciones que hacen algo real; el menú de inicio y la barra en 3 islas se añadirán cuando existan
            _contextMenu = new ContextMenuStrip();
            TrayMenuStyle.Apply(_contextMenu);

            ToolStripMenuItem privacyItem = TrayMenuStyle.Item("Modo privacidad", "", (s, e) =>
            {
                _eventBus.Publish(Events.PrivacyToggle);
            });
            privacyItem.ShortcutKeyDisplayString = "Ctrl+Alt+P";

            // Widgets, reloj, isla y privacidad se configuran en la ventana de ajustes
            ToolStripMenuItem settingsItem = TrayMenuStyle.Item("Ajustes…", "", (s, e) =>
            {
                SettingsWindow.Open(SettingsWindow.Page.Privacy);
            });

            ToolStripMenuItem exitItem = TrayMenuStyle.Item("Salir de Streeck Suite", "", (s, e) =>
            {
                Application.Exit();
            });

            _contextMenu.Items.Add(privacyItem);
            _contextMenu.Items.Add(settingsItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "Streeck Suite";
            _notifyIcon.Icon = LoadAppIcon(SystemInformation.SmallIconSize);
            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.DoubleClick += (s, e) =>
            {
                _eventBus.Publish(Events.NotchShowMessage, "✨ Streeck Suite en ejecución");
            };
        }

        /// <summary>
        /// Icono de la suite (assets\streeck.ico, incrustado al compilar) al tamaño pedido.
        /// </summary>
        internal static Icon LoadAppIcon(Size size)
        {
            try
            {
                using (Stream stream = typeof(TrayIconModule).Assembly.GetManifestResourceStream("StreeckSuite.streeck.ico"))
                {
                    if (stream != null) return new Icon(stream, size);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[TrayIconModule] No se pudo cargar el icono: {0}", ex.Message);
            }
            return SystemIcons.Application;
        }

    }
}
