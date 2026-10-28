/**
 * MÓDULO: Aero Glass & Privacidad
 * Aplica el desenfoque estilo Windows Vista sobre el fondo protegiendo la ventana activa
 */

window.AeroPrivacyModule = {
  id: 'aero_privacy',
  name: 'Aero Glass & Enfoque de Privacidad',
  version: '1.0.0',

  init({ eventBus, settings }) {
    this.bus = eventBus;
    this.settings = settings || {};
    this.backdropEl = document.getElementById('aeroBackdrop');
    this.activeWindowEl = document.getElementById('activeAppWindow');
    this.isActive = false;

    // Escuchar solicitudes de alternar privacidad
    this.bus.on('privacy:toggle', () => {
      this.togglePrivacy();
    });
  },

  start() {
    console.log('[AeroPrivacy] Filtro Aero Glass listo.');
  },

  togglePrivacy(forceState = null) {
    this.isActive = forceState !== null ? forceState : !this.isActive;

    const desktopScreen = document.getElementById('desktopScreen');
    if (desktopScreen) {
      desktopScreen.classList.toggle('privacy-active', this.isActive);
    }

    if (this.backdropEl && this.activeWindowEl) {
      if (this.isActive) {
        this.backdropEl.classList.add('active');
        this.activeWindowEl.classList.add('privacy-focused');
      } else {
        this.backdropEl.classList.remove('active');
        this.activeWindowEl.classList.remove('privacy-focused');
      }
    }

    this.bus.emit('privacy:state_changed', { active: this.isActive });
    return this.isActive;
  },

  stop() {
    this.togglePrivacy(false);
  }
};

if (window.CoreModuleManager) {
  window.CoreModuleManager.register(window.AeroPrivacyModule);
}
