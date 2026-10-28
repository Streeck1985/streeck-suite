# Streeck Suite — Estado del proyecto

> Última actualización: 27 de octubre de 2026 (Noche).
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
| **Dynamic Island / notch** | ✅ Isla flotante con animación elástica. Píldora cerrada (hora, punto pulsante o mini-anillo de temporizador, barras de audio, indicadores de mic/cámara, red y batería). Pestañas de panel expandido: **Música** (reproductor con carátula, progreso, botón de reproducción con anillo exterior en contorno), **Sonido** (volumen general + espectro FFT en tiempo real + volumen por app), **Clima** (pronóstico e iconos vectoriales), **Sistema** (CPU, RAM, GPU, uso de micro/cámara), **Tiempo** (Pomodoro / temporizadores rápidos), **Agenda** (próximos eventos), **Avisos** (historial de notificaciones) | `DynamicNotchModule.cs`, `IslandViews.cs`, `IslandAudioViews.cs`, `IslandNotificationViews.cs`, `IslandTimeViews.cs`, `IslandPainter.cs` |
| **Notificaciones reales de Windows** | ✅ Integración con Windows Toast (`UserNotificationListener`). Salen en la isla 4 s con icono real de la app (extraído de accesos directos), título y cuerpo. Historial en la pestaña Avisos | `NotificationService.cs`, `IslandNotificationViews.cs` |
| **Monitoreo de privacidad (Mic/Cámara)** | ✅ Puntos naranja (micrófono) y verde (cámara) en la píldora cerrada cuando una app los está utilizando, con alerta temporal | `CaptureMonitor.cs` |
| **Temporizador y Pomodoro** | ✅ Temporizadores configurables (5, 10, 15, 25 min...) y Pomodoro. Muestra el tiempo restante y mini anillo en la píldora cerrada | `TimerService.cs`, `IslandTimeViews.cs` |
| **Panel de Ajustes General** | ✅ Ventana nativa con diseño moderno de cristal oscuro: navegación lateral (General, Isla dinámica, Reloj 3D, Widgets, Avisos, Tiempo y agenda). Controles fluidos en tiempo real | `SettingsWindow.cs`, `SuiteSettings.cs` |
| **Modo privacidad Aero** | ✅ Desenfoca todo menos la ventana activa. Atajo **Ctrl+Alt+P** (o Ctrl+Alt+Shift+P / Ctrl+Alt+Q) o desde la bandeja | `AeroDwmModule.cs` |
| **Modo juego** | ✅ Oculta notch y widgets y pausa el reloj cuando una app ocupa toda la pantalla | `SystemMonitor.cs` |
| **Widgets de escritorio** | ✅ Tres ventanas independientes: Telemetría (CPU/RAM/GPU reales + consumo de la suite), Reproductor (cualquier app, con carátula) y Tareas (`notas.txt`). Cristal con fondo desenfocado | `DesktopWidgetsModule.cs`, `DesktopBackdrop.cs` |
| **Reloj 3D con profundidad** | ✅ Con **cualquier fondo**: la hora se dibuja detrás del sujeto principal o de objetos cercanos. Fecha encima, clima debajo. Ajustes integrados | `DepthClock*.cs`, `ClockSettingsForm.cs`, `Weather*.cs` |
| **Icono en la bandeja** | ✅ Menú estilizado con renderer propio: Privacidad, Ajustes, Simulador interactivo y Salir | `TrayIconModule.cs`, `TrayMenuRenderer.cs` |
| Barra de tareas en 3 islas | ❌ Solo existe en el simulador; el módulo nativo es un esqueleto | `TaskbarIslandModule.cs` |
| Menú de inicio Aero | ❌ Pendiente | — |
| Asistente de IA por voz/texto | ❌ Pendiente (planificado para Fase G) | — |
| Animaciones de minimizar | ❌ No viable para ventanas de otras apps sin inyectar código en Windows | — |

### Consumo medido (Ryzen 5 5600X, 1080p)
~0,25 % - 0,40 % de CPU total en reposo y ~150 MB de RAM con todos los servicios activos (incluyendo espectro de audio, listener de notificaciones y monitor de cámara/micrófono).

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
│   ├── SuiteSettings.cs        Ajustes unificados de la suite (JSON en LocalAppData)
│   ├── SystemMonitor.cs        Detección de pantalla completa (modo juego)
│   ├── TelemetrySampler.cs     CPU, RAM, GPU y consumo propio
│   ├── VisionModel.cs          Modelos ONNX ejecutados con Windows ML (descarga incluida)
│   └── WinRtAsync.cs           Espera de operaciones asíncronas de WinRT
└── Modules/
    ├── AeroDwmModule.cs        Modo privacidad + atajo global
    ├── AgendaService.cs        Servicio de eventos y citas de agenda
    ├── AudioService.cs         Control de volumen por aplicación y dispositivos
    ├── AudioSpectrum.cs        Captura de audio en loopback con análisis de frecuencias (FFT)
    ├── CaptureMonitor.cs       Detección de acceso al micrófono y cámara web
    ├── ClockSettingsForm.cs    Ventana de ajustes del reloj 3D
    ├── DepthClockLayers.cs     Ajustes del reloj, geometría y recorte con IA + filtro guiado
    ├── DepthClockModule.cs     Reloj 3D: detecta cambios de fondo y coordina el análisis
    ├── DesktopBackdrop.cs      Fondo de escritorio desenfocado para el cristal de los widgets
    ├── DesktopClockForm.cs     Ventana incrustada entre el fondo de Windows y los iconos
    ├── DesktopWidgetsModule.cs Widgets, posiciones y visibilidad
    ├── DynamicNotchModule.cs   Dynamic Island (gestión de capas, animaciones y estados)
    ├── IslandAudioViews.cs     Vistas de audio y mezclador dentro de la isla
    ├── IslandNotificationViews.cs Vistas de avisos y notificaciones en la isla
    ├── IslandPainter.cs        Utilidades de dibujo vectorial y tipografías para la isla
    ├── IslandTimeViews.cs      Vistas de temporizadores, Pomodoro y agenda
    ├── IslandViews.cs          Vistas principales (música, sistema, reposo, cabecera de pestañas)
    ├── MediaInfo.cs            Instantánea multimedia (título, artista, carátula, progreso)
    ├── MediaService.cs         Control multimedia global de Windows (SMTC)
    ├── NotificationService.cs  Servicio de escucha de notificaciones nativas de Windows
    ├── SettingsWindow.cs       Ventana general de ajustes de Streeck Suite
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
5. **Fase E: Bandeja de archivos y Búsqueda rápida**:
   - Bandeja (File Drop Tray): arrastrar archivos a la isla para dejarlos retenidos temporalmente y soltarlos en otra ventana.
   - Búsqueda rápida: atajo para desplegar una barra de búsqueda en la isla para apps y archivos.
6. **Fase F: Batería de auriculares y dispositivos Bluetooth**.
7. **Fase G: Asistente IA** (evaluar si panel web incrustado, API externa o modelo local).
8. **Menú de inicio Aero propio** y **Barra de tareas en 3 islas**.
