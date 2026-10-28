/**
 * MÓDULO: Dynamic Notch
 * Encapsula la lógica de la isla interactiva superior
 */

window.DynamicNotchModule = {
  id: 'dynamic_notch',
  name: 'Dynamic Island Notch',
  version: '1.0.0',

  init({ eventBus, settings }) {
    this.bus = eventBus;
    this.settings = settings || {};
    this.notchEl = document.getElementById('dynamicNotch');
    this.state = 'idle';

    // Suscripción a eventos de otros módulos
    this.bus.on('voice:state', (status) => {
      if (status === 'listening' || status === 'speaking') {
        this.setState('ai');
      } else {
        this.setState('idle');
      }
    });

    this.bus.on('media:update', (mediaInfo) => {
      this.setState('music');
    });

    this.bus.on('notification:push', (notif) => {
      this.setState('notif');
    });
  },

  start() {
    this.setState('idle');
    console.log('[DynamicNotch] Inicializado y listo en posición superior.');
  },

  setState(newState) {
    this.state = newState;
    if (this.notchEl) {
      this.notchEl.setAttribute('data-state', newState);
    }
    this.bus.emit('notch:state_changed', { state: newState });
  },

  stop() {
    this.setState('idle');
  }
};

// Auto-registro en el ModuleManager si está disponible
if (window.CoreModuleManager) {
  window.CoreModuleManager.register(window.DynamicNotchModule);
}
