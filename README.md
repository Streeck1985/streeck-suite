# 💎 Windows 11 Custom Suite (Modular & Native)

Suite de personalización modular de alto rendimiento para Windows 11 con estética **Aero Glass / Streeck**, **Barra en 3 Islas Flotantes**, **Dynamic Island / Notch**, **Reloj con Profundidad 3D** y **Auto-Suspensión en Videojuegos (0% CPU/GPU)**.

> 📋 **Estado actual, cómo compilar y próximos pasos:** ver [docs/ESTADO_DEL_PROYECTO.md](docs/ESTADO_DEL_PROYECTO.md).

---

## 🚀 Inicio Rápido

1. **Ejecutar la Suite Nativa:**
   Ejecuta en PowerShell (o haz doble clic en `iniciar_suite.bat`, que compila y lanza la suite en memoria):
   ```powershell
   .\bin\StreeckSuite.exe
   ```
   *Verás aparecer de inmediato la **Dynamic Island** en la parte superior central de tu pantalla y el icono en la bandeja del sistema (System Tray).*

2. **Recompilar el Proyecto:**
   ```powershell
   .\build.ps1
   ```

3. **Abrir el Simulador Interactivo:**
   ```powershell
   Start-Process msedge "file:///$PWD/index.html"
   ```

---

## 📁 Arquitectura del Proyecto

```text
Personalizar windows 11/
├── bin/
│   └── StreeckSuite.exe               <-- Ejecutable nativo (~30 KB)
├── src/
│   ├── Core/
│   │   ├── EventBus.cs              <-- Bus de eventos pub/sub tipado C#
│   │   ├── IModule.cs               <-- Interfaz base de módulos nativos
│   │   ├── ModuleManager.cs         <-- Gestor de ciclo de vida en caliente
│   │   ├── NativeMethods.cs         <-- P/Invoke DWM y Win32 (Acrylic, Mica, Blur)
│   │   └── SystemMonitor.cs         <-- Detección de juegos fullscreen (Zero-Impact)
│   ├── Modules/
│   │   ├── AeroDwmModule.cs         <-- Inyección de cristal Aero global en ventanas
│   │   ├── DynamicNotchModule.cs    <-- Ventana flotante Dynamic Island superior
│   │   ├── TaskbarIslandModule.cs   <-- Orquestación de la barra en 3 islas
│   │   ├── DepthClockModule.cs      <-- Reloj 3D sobre el fondo de escritorio real
│   │   └── TrayIconModule.cs        <-- Menú contextual en la bandeja del sistema
│   └── Program.cs                   <-- Punto de entrada WinMain
├── build.ps1                        <-- Pipeline de compilación automática
├── iniciar_suite.bat                <-- Lanzador en 1 clic
├── index.html                       <-- Simulador interactivo en vivo
├── style.css                        <-- Tokens de diseño y animaciones de resorte
└── app.js                           <-- Lógica del simulador
```

---

## ✅ Estado de la versión nativa frente al mockup (`index.html`)

| Función del mockup | Estado nativo |
| :--- | :--- |
| Dynamic Island / Notch | ✅ Isla flotante con animación elástica: reposo (hora, red, batería), música de Spotify con controles ⏮ ⏯ ⏭ reales y avisos de la suite. 🟡 Falta el estado de IA por voz y las notificaciones de otras apps |
| Modo juegos (auto-suspensión) | ✅ Oculta el notch cuando una ventana ocupa toda la pantalla |
| Icono en la bandeja | ✅ Menú, abrir simulador, ver registro y salir |
| Modo privacidad Aero (desenfoque real) | ✅ Lámina de cristal acrílico dinámico en Z-Order tras la ventana activa (Atajo: Ctrl+Alt+P) |
| Barra en 3 islas | ❌ Pendiente: no modifica la barra de tareas real |
| Reloj 3D con profundidad | ✅ Funciona con cualquier fondo. Dos IA locales vía Windows ML: detección del sujeto principal (U²-Net, 4 MB) y profundidad (Depth Anything V2, 95 MB). Si hay un sujeto claro (auto, persona) solo él pasa por delante; en paisajes, todo lo cercano. Posición, tamaño, fuente y modo ajustables desde la bandeja (🕒 Ajustar reloj 3D) |
| Widgets de escritorio | ✅ Telemetría real, Spotify y tareas (notas.txt). Cada widget se arrastra, recuerda su posición y se puede ocultar desde la bandeja (🧩 Widgets). Cristal con el fondo desenfocado y bordes suavizados |
| Menú de inicio, asistente de voz, animación de minimizar | ❌ Pendiente |

El registro de la suite se guarda en `%LOCALAPPDATA%\StreeckSuite\streecksuite.log`.

`build.ps1` también genera `config/settings.js` a partir de `config/settings.json`, para que el simulador lea la configuración al abrirse como `file:///`.
