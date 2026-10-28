namespace StreeckSuite.Core
{
    /// <summary>
    /// Nombres de todos los eventos del bus. Publicar y suscribirse siempre a través de estas
    /// constantes evita que un emisor y un receptor usen nombres distintos sin darse cuenta.
    /// </summary>
    public static class Events
    {
        // Sistema
        public const string AllModulesStarted = "SYSTEM:ALL_MODULES_STARTED";
        public const string AllModulesStopped = "SYSTEM:ALL_MODULES_STOPPED";
        public const string GameModeActive = "SYSTEM:GAME_MODE_ACTIVE";       // payload: bool

        // Notch
        public const string NotchShowMessage = "NOTCH:SHOW_MESSAGE";          // payload: string

        // Aero / privacidad
        public const string AeroSetStyle = "AERO:SET_STYLE";                  // payload: string
        public const string PrivacyToggle = "PRIVACY:TOGGLE";
        public const string PrivacyStateChanged = "PRIVACY:STATE_CHANGED";    // payload: bool

        // Reloj
        public const string ClockSetFont = "CLOCK:SET_FONT";                  // payload: string
        public const string ClockOpenSettings = "CLOCK:OPEN_SETTINGS";

        // Barra de tareas
        public const string TaskbarSetMode = "TASKBAR:SET_MODE";              // payload: string
        public const string TaskbarSetGap = "TASKBAR:SET_GAP";                // payload: int

        // Widgets de escritorio
        public const string WidgetsToggleVisibility = "WIDGETS:TOGGLE_VISIBILITY";
        public const string WidgetToggle = "WIDGETS:TOGGLE_ONE";              // payload: string (id del widget)
        public const string WidgetsResetLayout = "WIDGETS:RESET_LAYOUT";

        // Asistente de voz
        public const string VoiceTrigger = "VOICE_AI:TRIGGER";
    }
}
