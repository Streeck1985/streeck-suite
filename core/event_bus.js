/**
 * ============================================================================
 * CORE EVENT BUS - Patrón Publish / Subscribe Desacoplado
 * Permite que cualquier módulo emita y escuche eventos del sistema sin depender
 * directamente de otros módulos.
 * ============================================================================
 */

class EventBus {
  constructor() {
    this.listeners = new Map();
  }

  /**
   * Suscribirse a un evento
   * @param {string} event - Nombre del evento (ej: 'voice:listening', 'privacy:toggle')
   * @param {Function} callback - Función que responderá al evento
   * @returns {Function} Función para cancelar la suscripción
   */
  on(event, callback) {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, new Set());
    }
    this.listeners.get(event).add(callback);

    // Retorna función para desuscribirse limpiamente
    return () => {
      const subs = this.listeners.get(event);
      if (subs) {
        subs.delete(callback);
        if (subs.size === 0) this.listeners.delete(event);
      }
    };
  }

  /**
   * Emitir un evento con datos a todos los módulos suscritos
   * @param {string} event - Nombre del evento
   * @param {*} data - Información transmitida
   */
  emit(event, data = null) {
    if (!this.listeners.has(event)) return;
    const subs = this.listeners.get(event);
    subs.forEach(callback => {
      try {
        callback(data);
      } catch (err) {
        console.error(`[EventBus] Error en suscriptor de '${event}':`, err);
      }
    });
  }

  /**
   * Escuchar un evento una sola vez
   */
  once(event, callback) {
    const unsubscribe = this.on(event, (data) => {
      unsubscribe();
      callback(data);
    });
  }
}

// Instancia global del bus de eventos
window.CoreEventBus = new EventBus();
