# Streeck Suite — Estado del proyecto

> Última actualización: 28 de octubre de 2026 (fases A a F).
> Documento de traspaso: sirve para retomar el desarrollo en otra PC o con cualquier asistente (Antigravity, Claude, etc.).
> Léelo completo antes de tocar el código: la sección **Reglas para quien siga programando** evita los errores que ya ocurrieron.

---

## 1. Qué es

Suite de personalización para Windows 11 que convierte en una app nativa real lo que muestra el **simulador web** (`index.html`).
El simulador es la referencia de diseño; la app nativa es `bin\StreeckSuite.exe` (C#, WinForms + Win32/DWM + Windows ML).

El proyecto se llamaba "BloomSuite": se renombró a **Streeck Suite** y no debe quedar rastro del nombre anterior.

---

## 2. Cómo compilar y ejecutar

### Requisitos
- Windows 11 (probado en la build 26340, x64).
- .NET Framework 4.8 (viene con Windows). Se compila con `csc.exe` de .NET Framework, **sin** Visual Studio ni SDK de .NET.
- **Smart App Control desactivado**: bloquea cada `.exe` recién compilado porque no está firmado.
- Internet solo la primera vez (descarga de modelos de IA) y para el clima.

### Comandos (PowerShell, desde la carpeta del proyecto)

| Acción | Comando |
| :--- | :--- |
| Compilar | `powershell -ExecutionPolicy Bypass -File .\build.ps1` (cierra antes la suite: el `.exe` en uso no se puede sobrescribir) |
| Ejecutar | `Start-Process .\bin\StreeckSuite.exe` o doble clic en `iniciar_suite.bat` |
| Compilar si hay cambios y ejecutar | `.\run_suite.ps1` |
| Ejecutar y ver el registro inicial | `.\debug_run.ps1` |
| Cerrar la suite | Icono de la bandeja → **Salir** |

`build.ps1` también genera `config\settings.js` a partir de `config\settings.json` (el simulador lo necesita para funcionar abierto como `file:///`).

### Primera ejecución en otra PC
Los modelos de IA no están en el proyecto: la suite los descarga sola a `%LOCALAPPDATA%\StreeckSuite\models` (~100 MB en total) y avisa en el notch mientras tanto:
- `depth_anything_v2_small.onnx` (95 MB, Hugging Face) — profundidad.
- `u2netp.onnx` (4,4 MB, GitHub de rembg) — sujeto principal.

---

## 3. Qué funciona hoy

| Función del mockup / Suite | Estado | Dónde está |
| :--- | :--- | :--- |
| **Dynamic Island / notch** | ✅ Isla flotante con animación elástica. **Píldora cerrada**: hora, punto de reposo azul (o ecualizador de 5 barras con la música, o mini anillo con un temporizador), puntos de micrófono/cámara, red y batería. **Pestañas con iconos** (solo la activa lleva nombre; al pasar el ratón sale el nombre): **Música** (carátula, ecualizador real de 5 bandas, progreso, ⏮ ⏯ ⏭; el ⏯ es un círculo de contorno), **Sonido** (salidas, volumen general y por app), **Clima** (próximas 4 horas), **Sistema** (CPU, RAM, GPU, dispositivos Bluetooth con batería y qué app usa el micro/cámara), **Tiempo**, **Agenda**, **Bandeja** y **Avisos**. Tamaño (70–120 %) y opacidad ajustables | `DynamicNotchModule.cs`, `IslandViews.cs`, `IslandAudioViews.cs`, `IslandNotificationViews.cs`, `IslandTimeViews.cs`, `IslandTrayViews.cs`, `IslandDeviceViews.cs`, `IslandPainter.cs` |
| **Ecualizador real** | ✅ Captura en loopback de la salida de audio (sin micrófono), FFT de 1024 muestras y 5 bandas de graves a agudos. Solo trabaja mientras se dibujan las barras | `AudioSpectrum.cs` |
| **Notificaciones reales de Windows** | ✅ `UserNotificationListener` leído cada 1,5 s. Avisos en la isla con el **icono real de la app** (sacado de `shell:AppsFolder`, funciona con apps de la tienda y de escritorio), título y cuerpo; altura según el texto; cola con «+N». Historial en la pestaña Avisos. Apps silenciables en el panel | `NotificationService.cs`, `IslandNotificationViews.cs` |
| **Micrófono y cámara** | ✅ Micrófono: sesiones de grabación activas del audio de Windows (Core Audio). ⚠️ Cámara: solo por el registro `CapabilityAccessManager`, que en Windows 11 recientes (build 26340) **ya no se actualiza**: el punto verde no funciona ahí | `CaptureMonitor.cs` |
| **Temporizador y Pomodoro** | ✅ Atajos de 5/10/15/25 min, rueda ±1 min sobre el anillo, Pomodoro 25/5/15 (configurable). Aviso con sonido que se queda hasta hacer clic | `TimerService.cs`, `IslandTimeViews.cs` |
| **Agenda** | ✅ Hoy y mañana: calendario de Windows (cuenta Microsoft) + **Google Calendar por enlace iCal privado** (se pega desde el portapapeles en el panel). Botón «Unirse» si hay enlace de Teams/Meet/Zoom y aviso X min antes. Limitaciones: reglas de repetición poco comunes y eventos en otra zona horaria | `AgendaService.cs`, `IslandTimeViews.cs` |
| **Bandeja de archivos** | ✅ Arrastrar archivos sobre la píldora los guarda (solo la ruta); pestaña Bandeja con miniaturas; arrastrarlos fuera los copia (Mayús mueve) | `FileTrayService.cs`, `IslandTrayViews.cs`, `ShellImage.cs` |
| **Búsqueda rápida** | ✅ **Ctrl + Alt + S** (Ctrl+Alt+Espacio y Alt+Espacio los usa PowerToys). Apps del menú Inicio, archivos del índice de Windows (OLE DB `Search.CollatorDSO`, usando `System.ItemUrl`: `ItemPathDisplay` viene traducida y no sirve para abrir), calculadora y búsqueda web | `SearchService.cs`, `SearchWindow.cs` |
| **Batería Bluetooth** | ✅ Propiedades de dispositivo de Windows (SetupAPI) cada 10 s: aviso al conectar y de batería baja (20 %), fila de dispositivos en la pestaña Sistema | `BluetoothService.cs`, `IslandDeviceViews.cs` |
| **Asistente IA** | ✅ `AssistantWindow` (tarjeta como la isla, respuesta en vivo, botones sobre lo copiado, 🎙 con **Whisper local** (whisper.cpp b5454 + ggml-small-q5_1 en %LOCALAPPDATA%\StreeckSuite\whisper; graba con WASAPI y detecta el fin de la frase por volumen; el dictado de Windows para apps no recibe voz en Windows 11 reciente), leer en voz alta con SAPI). Motores: Ollama local o Gemini (clave cifrada con DPAPI en config.ini; si un modelo está saturado o tarda más de 8 s se pasa al siguiente y se recuerda en gemini_modelo.txt; si Gemini falla, contesta la local). Las órdenes de la suite se cumplen sin IA (`AssistantActions`) | `AssistantService.cs`, `AssistantWindow.cs`, `AssistantActions.cs`, `VoiceInput.cs` |
| **Panel de ajustes** | ✅ Ventana negra dibujada a mano (mockups P1–P5, C3, D3, E3). Páginas: **Privacidad, Isla, Avisos, Tiempo y agenda, Bandeja y búsqueda, Widgets, Reloj 3D**. Cambios en vivo | `SettingsWindow.cs`, `SuiteSettings.cs` |
| **Modo privacidad** | ✅ Desenfoca todo menos la ventana activa con **desenfoque propio** (0–40 px), oscurecimiento y tinte elegibles. Atajo **Ctrl+Alt+P**. ⚠️ La capa se excluye de las capturas para poder calcular el desenfoque: al **compartir la pantalla completa** los demás ven el fondo sin desenfocar | `AeroDwmModule.cs` |
| **Modo juego** | ✅ Oculta notch y widgets y pausa el reloj cuando una app ocupa toda la pantalla | `SystemMonitor.cs` |
| **Widgets de escritorio** | ✅ Tres ventanas independientes: Telemetría, Reproductor y Tareas (`notas.txt`). Cristal con fondo desenfocado; color de acento y opacidad desde el panel | `DesktopWidgetsModule.cs`, `DesktopBackdrop.cs` |
| **Reloj 3D con profundidad** | ✅ Con **cualquier fondo**: la hora detrás del sujeto principal o de lo cercano. Fecha encima, clima debajo, **segundero** opcional. Se configura en Ajustes → Reloj 3D | `DepthClock*.cs`, `DesktopClockForm.cs`, `Weather*.cs` |
| **Icono en la bandeja** | ✅ Icono «S» del usuario (`assets\streeck_512.png`). Menú: Modo privacidad, Ajustes…, Simulador interactivo y Salir | `TrayIconModule.cs`, `TrayMenuRenderer.cs` |
| Barra de tareas en 3 islas | ❌ Solo existe en el simulador; el módulo nativo es un esqueleto | `TaskbarIslandModule.cs` |
| Menú de inicio Aero | ❌ Pendiente | — |
| Asistente de IA por voz/texto | ❌ Pendiente (planificado para Fase G) | — |
| Animaciones de minimizar | ❌ No viable para ventanas de otras apps sin inyectar código en Windows | — |

### Consumo medido (Ryzen 5 5600X, 1080p)
~0,2 % - 0,5 % de CPU total y ~160 MB de RAM con todos los servicios activos (ecualizador con música, notificaciones, micrófono, Bluetooth). Pendiente de medir: el desenfoque propio del modo privacidad (el registro anota los ms por fotograma mientras está activo).

---

## 4. Arquitectura

```text
src/
├── Program.cs                  Punto de entrada: instancia única, DPI, registro de módulos
├── Core/
│   ├── CoreAudio.cs            Interfaces COM de WASAPI y mezclador de audio de Windows
│   ├── EventBus.cs             Bus pub/sub entre módulos (los módulos no se llaman entre sí)
│   ├── Events.cs               Nombres de TODOS los eventos (usar siempre estas constantes)
│   ├── IModule.cs              Interfaz de módulo: Initialize / Start / Stop
│   ├── LayeredWindow.cs        Ventana con transparencia por píxel (bordes suaves y sombras)
│   ├── Log.cs                  Registro en %LOCALAPPDATA%\StreeckSuite\streecksuite.log
│   ├── ModuleManager.cs        Ciclo de vida de los módulos
│   ├── NativeMethods.cs        P/Invoke de Win32, DWM y GDI
│   ├── SuiteSettings.cs        Ajustes del panel: %LOCALAPPDATA%\StreeckSuite\config.ini (el reloj usa reloj.ini;
│   │                           apps de avisos: avisos_apps.txt; bandeja: bandeja.txt; widgets: widgets.ini)
│   ├── SystemMonitor.cs        Detección de pantalla completa (modo juego)
│   ├── TelemetrySampler.cs     CPU, RAM, GPU y consumo propio
│   ├── VisionModel.cs          Modelos ONNX ejecutados con Windows ML (descarga incluida)
│   └── WinRtAsync.cs           Espera de operaciones asíncronas de WinRT
└── Modules/
    ├── AeroDwmModule.cs        Modo privacidad + atajo global
    ├── AgendaService.cs        Servicio de eventos y citas de agenda
    ├── AudioService.cs         Control de volumen por aplicación y dispositivos
    ├── AudioSpectrum.cs        Captura de audio en loopback con análisis de frecuencias (FFT)
    ├── BluetoothService.cs     Dispositivos Bluetooth y su batería (SetupAPI)
    ├── CaptureMonitor.cs       Micrófono (sesiones de Core Audio) y cámara (registro)
    ├── DepthClockLayers.cs     Ajustes del reloj, geometría y recorte con IA + filtro guiado
    ├── DepthClockModule.cs     Reloj 3D: detecta cambios de fondo y coordina el análisis
    ├── DesktopBackdrop.cs      Fondo de escritorio desenfocado para el cristal de los widgets
    ├── DesktopClockForm.cs     Ventana incrustada entre el fondo de Windows y los iconos
    ├── DesktopWidgetsModule.cs Widgets, posiciones y visibilidad
    ├── DynamicNotchModule.cs   Dynamic Island (estados, animaciones, avisos, arrastrar archivos) + atajo de la búsqueda
    ├── FileTrayService.cs      Bandeja de archivos (rutas guardadas, miniaturas)
    ├── IslandAudioViews.cs     Vistas de audio y mezclador dentro de la isla
    ├── IslandDeviceViews.cs    Aviso Bluetooth y dibujo de la batería
    ├── IslandNotificationViews.cs Vistas de avisos y notificaciones en la isla
    ├── IslandPainter.cs        Utilidades de dibujo vectorial y tipografías para la isla
    ├── IslandTimeViews.cs      Vistas de temporizadores, Pomodoro y agenda
    ├── IslandTrayViews.cs      Pestaña Bandeja y zona para soltar archivos
    ├── IslandViews.cs          Vistas principales (música, sistema, reposo, cabecera de pestañas)
    ├── MediaInfo.cs            Instantánea multimedia (título, artista, carátula, progreso)
    ├── MediaService.cs         Control multimedia global de Windows (SMTC)
    ├── NotificationService.cs  Servicio de escucha de notificaciones nativas de Windows
    ├── SearchService.cs        Motor de la búsqueda: apps, archivos (índice de Windows), calculadora, web
    ├── SearchWindow.cs         Ventana de la búsqueda rápida y su atajo global
    ├── SettingsWindow.cs       Ventana general de ajustes de Streeck Suite
    ├── ShellImage.cs           Miniaturas e iconos del shell y lista de apps del menú Inicio
    ├── TaskbarIslandModule.cs  Esqueleto, sin funcionalidad real
    ├── TimerService.cs         Lógica de temporizador de cuenta atrás y Pomodoro
    ├── TrayIconModule.cs       Menú de la bandeja del sistema
    ├── TrayMenuRenderer.cs     Renderizado estilizado oscuro para los menús de la bandeja
    ├── WeatherIcons.cs         Iconos vectoriales del clima
    └── WeatherService.cs       Clima (Open-Meteo) + ubicación aproximada por IP
```

---

## 5. Reglas para quien siga programando

1. **Antes de escribir código para una nueva función o cambio visual, PREGUNTA al usuario y muestra MOCKUPS**: regla de oro del proyecto acordada con el usuario. No encadenes cambios visuales grandes por cuenta propia.
2. **Solo C# 5**: el compilador es el de .NET Framework 4 (`csc.exe`). Nada de `?.`, `$"..."`, miembros con `=>`, `nameof` ni inicializadores de propiedades automáticas.
3. **Cierra la suite antes de compilar** (`Get-Process StreeckSuite -ErrorAction SilentlyContinue | Stop-Process -Force`).
4. **Nunca lances la suite en primer plano dentro de la terminal de un asistente**: usa `Start-Process` o `run_suite.ps1`.
5. **Usa las constantes de `Events.cs`**; no escribas nombres de eventos a mano.
6. **Errores al registro** con `Log.Error(...)`; `Debug.WriteLine` desaparece al compilar.
7. **No reintroducir el nombre "Bloom"** en código, textos ni archivos.
8. **Comprueba el consumo** tras cambios en animaciones o sondeos (objetivo: <0,5 % de CPU total en reposo).

---

## 6. Próximos pasos (Hoja de ruta acordada)

1. ~~Música de cualquier app con carátula real~~ ✅ Hecho (Fase A).
2. ~~Panel de ajustes general~~ ✅ Hecho (Fase B).
3. ~~Notificaciones de Windows e indicadores de privacidad (Mic/Cam)~~ ✅ Hecho (Fase C).
4. ~~Temporizador / Pomodoro y Agenda~~ ✅ Hecho (Fase D).
5. ~~Bandeja de archivos y búsqueda rápida~~ ✅ Hecho (Fase E).
6. ~~Batería de dispositivos Bluetooth~~ ✅ Hecho (Fase F).
7. ~~Asistente IA con voz~~ ✅ Hecho (Fase G): Ollama local (llama3.1:8b en la GPU, ~60 tokens/s) o Gemini con clave gratuita; Ctrl+Alt+A o «Preguntar a la IA» en la búsqueda; dictado de Windows; órdenes directas (temporizadores, música, volumen, abrir apps, buscar, privacidad). Pendiente opcional: Whisper local.
8. **Menú de inicio Aero propio** y **Barra de tareas en 3 islas**.

Pendientes sueltos: detectar la cámara en Windows 11 reciente sin el registro; probar Google Calendar con un enlace real;
medir el desenfoque del modo privacidad; opción "modo presentación" para que se vea desenfocado al compartir pantalla.

### Herramientas de desarrollo
`bin\StreeckSuite.exe --preview-island <carpeta>`, `--preview-settings <carpeta>` y `--preview-search <carpeta> <texto>...`
guardan en PNG la isla, el panel de ajustes y la búsqueda, sin mostrar ventanas.
