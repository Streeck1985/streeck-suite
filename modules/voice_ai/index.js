/**
 * MÓDULO: Voice AI
 * Reconocimiento de voz, invocación del modelo y orquestación por voz
 */

window.VoiceAIModule = {
  id: 'voice_ai',
  name: 'Asistente IA por Voz',
  version: '1.0.0',

  init({ eventBus, settings }) {
    this.bus = eventBus;
    this.settings = settings || {};
    this.transcriptEl = document.getElementById('aiTranscript');

    // Escuchar disparadores externos (atajo de teclado o click)
    this.bus.on('voice:trigger', () => {
      this.activateVoiceSession();
    });
  },

  start() {
    console.log('[VoiceAI] Motor de voz cargado con éxito.');
  },

  activateVoiceSession() {
    this.bus.emit('voice:state', 'listening');
    if (this.transcriptEl) {
      this.transcriptEl.textContent = "Escuchando... Di un comando...";
    }

    // Simulación de reconocimiento y ejecución
    setTimeout(() => {
      this.handleVoiceCommand("activa modo privacidad");
    }, 1800);
  },

  handleVoiceCommand(commandText) {
    this.bus.emit('voice:state', 'speaking');
    this.bus.emit('voice:command_received', { command: commandText });

    if (commandText.includes("privacidad") || commandText.includes("aero")) {
      if (this.transcriptEl) {
        this.transcriptEl.textContent = `"Entendido, activando modo privacidad Aero Glass..."`;
      }
      this.bus.emit('privacy:toggle');
    }

    setTimeout(() => {
      this.bus.emit('voice:state', 'idle');
    }, 2500);
  },

  stop() {
    this.bus.emit('voice:state', 'idle');
  }
};

if (window.CoreModuleManager) {
  window.CoreModuleManager.register(window.VoiceAIModule);
}
