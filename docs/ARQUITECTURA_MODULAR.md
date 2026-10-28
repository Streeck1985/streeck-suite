# 🏗️ Arquitectura Modular del Sistema (Windows 11 Custom Suite)

Esta arquitectura está diseñada para ser **completamente extensible**. Cada componente (el notch, la IA por voz, el reloj de fondo 3D, el modo Aero y los widgets) funciona de forma independiente. Puedes añadir o retirar módulos en cualquier momento sin afectar el resto del sistema.

---

## 📁 Estructura del Proyecto

```text
Personalizar windows 11/
├── config/
│   └── settings.json                 <-- Configuración global (habilita/deshabilita módulos)
├── core/
│   ├── event_bus.js                  <-- Bus de eventos desacoplado (pub/sub global)
│   └── module_manager.js             <-- Cargador y gestor del ciclo de vida
├── modules/
│   ├── dynamic_notch/                <-- Módulo 1: Dynamic Island y notificaciones
│   ├── voice_ai/                     <-- Módulo 2: Asistente IA por voz (Gemini)
│   ├── depth_wallpaper/              <-- Módulo 3: Reloj con profundidad 3D
│   ├── aero_privacy/                 <-- Módulo 4: Modo Aero Glass de privacidad
│   ├── streeck_taskbar/                <-- Módulo 5: Barra de tareas minimalista
│   └── desktop_widgets/              <-- Módulo 6: Widgets de hardware y clima
├── docs/
│   └── ARQUITECTURA_MODULAR.md       <-- Esta guía
├── index.html                        <-- Interfaz visual principal y simulador
├── style.css                         <-- Sistema de diseño y tokens de Aero Glass
└── app.js                            <-- Inicializador y enlace general
```

---

## ⚡ Cómo Crear un Nuevo Módulo

Para agregar una nueva funcionalidad (por ejemplo: un módulo de control de volumen con gestos, un gestor de portapapeles o un visualizador de audio):

### 1. Crea la carpeta en `modules/`
Ejemplo: `modules/audio_visualizer/`

### 2. Agrega el manifiesto `module.json`
```json
{
  "id": "audio_visualizer",
  "name": "Visualizador de Audio Dinámico",
  "version": "1.0.0",
  "description": "Muestra barras de audio reactivas en el escritorio",
  "eventsEmitted": ["visualizer:active"],
  "eventsSubscribed": ["media:update", "voice:state"]
}
```

### 3. Implementa `index.js` con el ciclo de vida estándar
```javascript
window.AudioVisualizerModule = {
  id: 'audio_visualizer',
  name: 'Visualizador de Audio Dinámico',
  version: '1.0.0',

  // 1. Inicialización: recibir configuración y bus de eventos
  init({ eventBus, settings }) {
    this.bus = eventBus;
    this.settings = settings;

    // Escuchar eventos de otros módulos
    this.bus.on('voice:state', (status) => {
      console.log('El estado de la voz cambió a:', status);
    });
  },

  // 2. Inicio del módulo
  start() {
    console.log('[AudioVisualizer] Iniciado correctamente');
  },

  // 3. Detener / limpiar recursos
  stop() {
    console.log('[AudioVisualizer] Detenido');
  }
};

// Registro automático en el núcleo
if (window.CoreModuleManager) {
  window.CoreModuleManager.register(window.AudioVisualizerModule);
}
```

### 4. Actívalo en `config/settings.json`
```json
"audio_visualizer": {
  "enabled": true,
  "settings": {
    "sensitivity": 80,
    "color": "#00d2ff"
  }
}
```

---

## 📡 Bus de Eventos Disponible (CoreEventBus)

Los módulos no se llaman directamente entre sí; se comunican mediante eventos. Esto garantiza que si un módulo se desactiva, nada se rompe:

| Evento | Emitido por | Utilizado por | Descripción |
| :--- | :--- | :--- | :--- |
| `voice:trigger` | Barra / Teclado | `voice_ai` | Inicia la escucha por voz de la IA. |
| `voice:state` | `voice_ai` | `dynamic_notch` | Estado de la IA (`listening`, `speaking`, `idle`). |
| `privacy:toggle` | `voice_ai` / Tray / Teclado | `aero_privacy` | Alterna el desenfoque Aero Glass en segundo plano. |
| `wallpaper:cycle`| `voice_ai` / Controles | `depth_wallpaper` | Cambia la imagen y silueta de fondo. |
| `media:update` | Windows Media Bridge | `dynamic_notch` | Actualiza la canción actual en el notch. |
