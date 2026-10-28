/**
 * MÓDULO: Depth Wallpaper
 * Renderizado de capas: Fondo + Reloj tipográfico + Sujeto recortado
 */

window.DepthWallpaperModule = {
  id: 'depth_wallpaper',
  name: 'Reloj de Profundidad 3D',
  version: '1.0.0',

  init({ eventBus, settings }) {
    this.bus = eventBus;
    this.settings = settings || {};
    this.bgLayer = document.getElementById('bgLayer');
    this.subjectLayer = document.getElementById('subjectLayer');
    this.clockEl = document.getElementById('depthClock');
    this.isDepthEnabled = this.settings.depthEffectActive ?? true;
    this.presets = [];
    this.presetIdx = 0;

    this.bus.on('wallpaper:cycle', () => {
      this.cyclePreset();
    });

    this.bus.on('wallpaper:toggle_depth', () => {
      this.toggleDepth();
    });
  },

  start() {
    this.toggleDepth(this.isDepthEnabled);
    console.log('[DepthWallpaper] Motor de capas de profundidad activo.');
  },

  /**
   * Recibe la lista de fondos { name, bg, subjectSvg } y aplica el primero
   */
  setPresets(presets) {
    this.presets = presets || [];
    this.presetIdx = 0;
    this.applyPreset(this.presetIdx);
  },

  applyPreset(idx) {
    const preset = this.presets[idx];
    if (!preset) return;
    if (this.bgLayer) this.bgLayer.style.background = preset.bg;
    if (this.subjectLayer) this.subjectLayer.style.backgroundImage = preset.subjectSvg || 'none';
    this.bus.emit('wallpaper:preset_changed', { index: idx, name: preset.name });
  },

  cyclePreset() {
    if (this.presets.length === 0) return;
    this.presetIdx = (this.presetIdx + 1) % this.presets.length;
    this.applyPreset(this.presetIdx);
  },

  toggleDepth(forceState = null) {
    this.isDepthEnabled = forceState !== null ? forceState : !this.isDepthEnabled;
    if (this.subjectLayer) {
      this.subjectLayer.style.opacity = this.isDepthEnabled ? '1' : '0';
    }
    this.bus.emit('wallpaper:depth_toggled', { enabled: this.isDepthEnabled });
    return this.isDepthEnabled;
  },

  setClockPosition(y) {
    if (this.clockEl) {
      this.clockEl.style.top = `${y}px`;
    }
  },

  stop() {
    if (this.subjectLayer) this.subjectLayer.style.opacity = '1';
  }
};

if (window.CoreModuleManager) {
  window.CoreModuleManager.register(window.DepthWallpaperModule);
}
