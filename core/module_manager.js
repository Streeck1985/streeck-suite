/**
 * ============================================================================
 * CORE MODULE MANAGER - Gestor del Ciclo de Vida de Módulos
 * Carga, inicializa, habilita, deshabilita y monitoriza módulos de manera
 * dinámica y aislada.
 * ============================================================================
 */

class ModuleManager {
  constructor(eventBus) {
    this.eventBus = eventBus;
    this.modules = new Map();
    this.config = {};
  }

  /**
   * Cargar configuración base del sistema
   */
  async loadConfig(configData) {
    this.config = configData || {};
    console.log('[ModuleManager] Configuración cargada con éxito');
  }

  /**
   * Registrar un nuevo módulo en la suite
   * @param {Object} moduleDefinition - Definición del módulo con id, name, init, start, stop
   */
  register(moduleDefinition) {
    const { id, name, version = '1.0.0', init, start, stop } = moduleDefinition;

    if (!id || typeof init !== 'function') {
      throw new Error(`[ModuleManager] Módulo inválido. Se requiere 'id' e 'init()'`);
    }

    if (this.modules.has(id)) {
      console.warn(`[ModuleManager] El módulo '${id}' ya está registrado. Sobrescribiendo.`);
    }

    const moduleRecord = {
      id,
      name: name || id,
      version,
      instance: moduleDefinition,
      status: 'registered' // 'registered', 'initialized', 'running', 'stopped', 'error'
    };

    this.modules.set(id, moduleRecord);
    console.log(`[ModuleManager] Módulo registrado: ${name} [v${version}]`);
    return this;
  }

  /**
   * Inicializar e iniciar todos los módulos habilitados según la configuración
   */
  async startAll() {
    console.log('[ModuleManager] Iniciando módulos habilitados...');

    for (const [id, mod] of this.modules) {
      const isEnabled = this.config.modules?.[id]?.enabled ?? true;

      if (isEnabled) {
        await this.startModule(id);
      } else {
        mod.status = 'disabled';
        console.log(`[ModuleManager] Módulo '${id}' omitido (deshabilitado en configuración).`);
      }
    }

    this.eventBus.emit('system:ready', {
      activeModules: Array.from(this.modules.values()).filter(m => m.status === 'running')
    });
  }

  /**
   * Iniciar un módulo individual
   */
  async startModule(id) {
    const mod = this.modules.get(id);
    if (!mod) return console.error(`[ModuleManager] Módulo '${id}' no encontrado.`);

    try {
      if (!mod.initialized) {
        // Los settings se leen aquí y no en register(): los módulos se registran al cargar
        // sus <script>, antes de que app.js haya cargado la configuración
        await mod.instance.init({
          eventBus: this.eventBus,
          settings: this.config.modules?.[id]?.settings || {},
          manager: this
        });
        mod.initialized = true;
        mod.status = 'initialized';
      }

      if (typeof mod.instance.start === 'function') {
        await mod.instance.start();
      }
      mod.status = 'running';
      console.log(`[ModuleManager] ✓ Módulo '${mod.name}' en ejecución.`);
      this.eventBus.emit('module:started', { id, name: mod.name });
    } catch (err) {
      mod.status = 'error';
      console.error(`[ModuleManager] ✗ Error al iniciar módulo '${id}':`, err);
    }
  }

  /**
   * Detener un módulo individual
   */
  async stopModule(id) {
    const mod = this.modules.get(id);
    if (!mod || mod.status !== 'running') return;

    try {
      if (typeof mod.instance.stop === 'function') {
        await mod.instance.stop();
      }
      mod.status = 'stopped';
      console.log(`[ModuleManager] Módulo '${mod.name}' detenido.`);
      this.eventBus.emit('module:stopped', { id, name: mod.name });
    } catch (err) {
      console.error(`[ModuleManager] Error al detener módulo '${id}':`, err);
    }
  }

  /**
   * Obtener lista de módulos registrados y su estado
   */
  listModules() {
    return Array.from(this.modules.values()).map(m => ({
      id: m.id,
      name: m.name,
      version: m.version,
      status: m.status
    }));
  }
}

// Instancia global
window.CoreModuleManager = new ModuleManager(window.CoreEventBus);
