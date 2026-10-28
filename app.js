/* ==========================================================================
   WINDOWS 11 CUSTOM SUITE - INTERACTIVE ENGINE & CUSTOMIZER
   3 Islas Flotantes, Animación de Minimizar, Fuentes de Windows y Widgets
   ========================================================================== */

document.addEventListener('DOMContentLoaded', async () => {

  // --- 1. INICIALIZAR NÚCLEO MODULAR ---
  const bus = window.CoreEventBus;

  if (window.CoreModuleManager) {
    // config/settings.js lo genera build.ps1 a partir de settings.json, porque
    // fetch() no funciona al abrir el simulador como file:///
    let config = window.SUITE_CONFIG;
    if (!config) {
      try {
        const response = await fetch('config/settings.json');
        if (response.ok) config = await response.json();
      } catch (e) {
        console.warn('[App] No se pudo leer config/settings.json; usando valores por defecto');
      }
    }
    await window.CoreModuleManager.loadConfig(config);
    await window.CoreModuleManager.startAll();
  }

  // --- 2. NAVEGACIÓN ENTRE LAS 3 PESTAÑAS ---
  const navTabs = document.querySelectorAll('.nav-tab');
  const viewSections = {
    desktop: document.getElementById('viewDesktop'),
    showcase: document.getElementById('viewShowcase'),
    code: document.getElementById('viewCode')
  };

  navTabs.forEach(tab => {
    tab.addEventListener('click', () => {
      const targetTab = tab.dataset.tab;
      navTabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');

      Object.keys(viewSections).forEach(key => {
        if (viewSections[key]) {
          viewSections[key].classList.toggle('active', key === targetTab);
        }
      });
    });
  });

  // --- 3. ELEMENTOS DOM ---
  const dynamicNotch = document.getElementById('dynamicNotch');
  const clockTime = document.getElementById('clockTime');
  const clockDate = document.getElementById('clockDate');
  const depthClock = document.getElementById('depthClock');
  const wallpaperSystem = document.getElementById('wallpaperSystem');
  const bgLayer = document.getElementById('bgLayer');
  const subjectLayer = document.getElementById('subjectLayer');
  const aeroBackdrop = document.getElementById('aeroBackdrop');
  const activeAppWindow = document.getElementById('activeAppWindow');
  const streeckTaskbar = document.getElementById('streeckTaskbar');
  const desktopWidgets = document.getElementById('desktopWidgets');
  const controlPanel = document.getElementById('controlPanel');
  const togglePanelBtn = document.getElementById('togglePanelBtn');

  // Previews de Código
  const activeFontPreview = document.getElementById('activeFontPreview');
  const cfgTaskbarFormat = document.getElementById('cfgTaskbarFormat');
  const cfgFontsCount = document.getElementById('cfgFontsCount');

  // Botones
  const quickAeroToggle = document.getElementById('quickAeroToggle');
  const quickAeroStatus = document.getElementById('quickAeroStatus');
  const toggleAeroBtn = document.getElementById('toggleAeroBtn');
  const trayAeroBtn = document.getElementById('trayAeroBtn');
  const toggleDepthBtn = document.getElementById('toggleDepthBtn');
  const changeWallpaperBtn = document.getElementById('changeWallpaperBtn');
  const explode3DBtn = document.getElementById('explode3DBtn');
  const playPauseBtn = document.getElementById('playPauseBtn');
  const aiTranscript = document.getElementById('aiTranscript');

  // Ventana y Minimizar
  const winMinBtn = document.getElementById('winMinBtn');
  const winMaxBtn = document.getElementById('winMaxBtn');
  const winCloseBtn = document.getElementById('winCloseBtn');
  const taskbarVsCodeBtn = document.getElementById('taskbarVsCodeBtn');
  const testMinimizeBtn = document.getElementById('testMinimizeBtn');
  const testMaximizeBtn = document.getElementById('testMaximizeBtn');

  // Sliders del Reloj
  const clockYRange = document.getElementById('clockYRange');
  const clockXRange = document.getElementById('clockXRange');
  const clockSizeRange = document.getElementById('clockSizeRange');
  const clockYLabel = document.getElementById('clockYLabel');
  const clockXLabel = document.getElementById('clockXLabel');
  const clockSizeLabel = document.getElementById('clockSizeLabel');

  // Telemetría
  const cpuVal = document.getElementById('cpuVal');
  const ramVal = document.getElementById('ramVal');
  const gpuVal = document.getElementById('gpuVal');
  const cpuBar = document.getElementById('cpuBar');
  const ramBar = document.getElementById('ramBar');
  const gpuBar = document.getElementById('gpuBar');

  // --- 4. FORMATO DE LA BARRA: 3 ISLAS, GAP Y MODO EXTREMOS ---
  const tbFormatButtons = document.querySelectorAll('[data-tb-format]');
  const islandGapRange = document.getElementById('islandGapRange');
  const islandGapLabel = document.getElementById('islandGapLabel');
  const gapPresetButtons = document.querySelectorAll('[data-gap-preset]');

  const formatNames = {
    'split': '3 Islas / Globos Flotantes 🏝️',
    'dock': 'Cápsula Flotante (Dock) 💊',
    'full': 'Barra Completa (Edge to Edge) 📏'
  };

  tbFormatButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      tbFormatButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      const format = btn.dataset.tbFormat;
      streeckTaskbar.classList.remove('taskbar-mode-split', 'taskbar-mode-dock', 'taskbar-mode-full');
      streeckTaskbar.classList.add(`taskbar-mode-${format}`);

      if (cfgTaskbarFormat) {
        cfgTaskbarFormat.textContent = `"${formatNames[format]}"`;
      }
    });
  });

  // Slider de separación libre entre islas
  if (islandGapRange) {
    islandGapRange.addEventListener('input', (e) => {
      const val = e.target.value;
      streeckTaskbar.classList.remove('islands-edges');
      streeckTaskbar.style.gap = `${val}px`;
      if (islandGapLabel) islandGapLabel.textContent = `${val}px`;
      gapPresetButtons.forEach(b => b.classList.remove('active'));
    });
  }

  // Presets de separación (Juntas 0px, Flotantes 14px, A los Extremos)
  gapPresetButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      gapPresetButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      const preset = btn.dataset.gapPreset;
      if (preset === 'edges') {
        streeckTaskbar.classList.add('islands-edges');
        streeckTaskbar.style.gap = '';
        if (islandGapLabel) islandGapLabel.textContent = 'A los Extremos';
      } else {
        streeckTaskbar.classList.remove('islands-edges');
        streeckTaskbar.style.gap = `${preset}px`;
        if (islandGapRange) islandGapRange.value = preset;
        if (islandGapLabel) islandGapLabel.textContent = `${preset}px`;
      }
    });
  });

  // --- 5. LAS 7 ANIMACIONES DE MINIMIZAR / RESTAURAR Y CONTROL DE VELOCIDAD ---
  const animSelect = document.getElementById('animSelect');
  const animDurationRange = document.getElementById('animDurationRange');
  const animDurationLabel = document.getElementById('animDurationLabel');
  const speedButtons = document.querySelectorAll('[data-speed]');

  let currentAnimation = 'anim-genie';
  let currentDuration = 0.85; // Duración cinemática por defecto

  activeAppWindow.classList.add(currentAnimation);
  document.documentElement.style.setProperty('--anim-duration', `${currentDuration}s`);

  // Control deslizante de velocidad / cámara lenta
  function setAnimationDuration(sec) {
    currentDuration = sec;
    document.documentElement.style.setProperty('--anim-duration', `${sec}s`);
    if (animDurationRange) animDurationRange.value = sec;

    if (animDurationLabel) {
      if (sec < 0.5) animDurationLabel.textContent = `${sec}s (Rápida)`;
      else if (sec <= 1.2) animDurationLabel.textContent = `${sec}s (Cinemática)`;
      else animDurationLabel.textContent = `${sec}s (Cámara Lenta 🐢)`;
    }
  }

  if (animDurationRange) {
    animDurationRange.addEventListener('input', (e) => {
      setAnimationDuration(parseFloat(e.target.value));
      speedButtons.forEach(b => b.classList.remove('active'));
    });
  }

  speedButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      speedButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      setAnimationDuration(parseFloat(btn.dataset.speed));
    });
  });

  // Selector de las 7 animaciones
  if (animSelect) {
    animSelect.addEventListener('change', (e) => {
      const newAnim = e.target.value;
      activeAppWindow.classList.remove(
        'anim-genie', 'anim-jelly', 'anim-vortex', 'anim-origami',
        'anim-abyss', 'anim-spring', 'anim-holo'
      );
      activeAppWindow.classList.add(newAnim);
      currentAnimation = newAnim;

      // Demostración automática para apreciar de inmediato la física elegida
      if (!isWindowMinimized) {
        toggleMinimizeWindow();
        setTimeout(toggleMinimizeWindow, Math.max(currentDuration * 1000 + 150, 700));
      }
    });
  }

  let isWindowMinimized = false;
  let isWindowMaximized = false;

  function toggleMinimizeWindow() {
    isWindowMinimized = !isWindowMinimized;

    if (isWindowMinimized) {
      activeAppWindow.classList.remove('restoring');
      activeAppWindow.classList.add('minimized');
    } else {
      activeAppWindow.classList.remove('minimized');
      activeAppWindow.classList.add('restoring');
      setTimeout(() => {
        activeAppWindow.classList.remove('restoring');
      }, currentDuration * 1000);
    }

    // Actualizar indicador activo en el icono de la barra de tareas
    const dot = taskbarVsCodeBtn.querySelector('.active-dot');
    if (dot) {
      dot.style.opacity = isWindowMinimized ? '0.3' : '1';
      dot.style.transform = isWindowMinimized ? 'scale(0.8)' : 'scale(1)';
    }

    if (testMinimizeBtn) {
      testMinimizeBtn.textContent = isWindowMinimized ? '🔼 Restaurar' : '🔽 Probar Minimizar';
      testMinimizeBtn.classList.toggle('active', isWindowMinimized);
    }
  }

  // --- CONTROLES DE INTENSIDAD DEL MODO PRIVACIDAD ---
  const privacyBlurRange = document.getElementById('privacyBlurRange');
  const privacyBlurLabel = document.getElementById('privacyBlurLabel');
  const privacyDimRange = document.getElementById('privacyDimRange');
  const privacyDimLabel = document.getElementById('privacyDimLabel');

  if (privacyBlurRange) {
    privacyBlurRange.addEventListener('input', (e) => {
      const val = e.target.value;
      document.documentElement.style.setProperty('--privacy-blur', `${val}px`);
      if (privacyBlurLabel) privacyBlurLabel.textContent = `${val}px`;
    });
  }

  if (privacyDimRange) {
    privacyDimRange.addEventListener('input', (e) => {
      const val = e.target.value;
      document.documentElement.style.setProperty('--privacy-dim', `${val / 100}`);
      if (privacyDimLabel) privacyDimLabel.textContent = `${val}%`;
    });
  }

  function toggleMaximizeWindow() {
    isWindowMaximized = !isWindowMaximized;
    activeAppWindow.classList.toggle('maximized', isWindowMaximized);
    testMaximizeBtn.textContent = isWindowMaximized ? '❐ Desmaximizar' : '⏹ Maximizar';
    testMaximizeBtn.classList.toggle('active', isWindowMaximized);
    winMaxBtn.textContent = isWindowMaximized ? '❐' : '□';
  }

  winMinBtn.addEventListener('click', toggleMinimizeWindow);
  testMinimizeBtn.addEventListener('click', toggleMinimizeWindow);

  winMaxBtn.addEventListener('click', toggleMaximizeWindow);
  testMaximizeBtn.addEventListener('click', toggleMaximizeWindow);

  winCloseBtn.addEventListener('click', () => {
    activeAppWindow.style.opacity = '0';
    activeAppWindow.style.transform = 'translate(-50%, -50%) scale(0.9)';
    setTimeout(() => {
      activeAppWindow.style.display = 'none';
    }, 300);
  });

  // Clic en el icono de VS Code en la barra de tareas minimiza o restaura
  taskbarVsCodeBtn.addEventListener('click', () => {
    if (activeAppWindow.style.display === 'none') {
      activeAppWindow.style.display = 'block';
      setTimeout(() => {
        activeAppWindow.style.opacity = '1';
        activeAppWindow.style.transform = 'translate(-50%, -50%) scale(1)';
      }, 50);
      isWindowMinimized = false;
      return;
    }
    toggleMinimizeWindow();
  });

  // --- 6. LECTURA DE FUENTES DE WINDOWS (NATIVAS + LOCALES) ---
  const windowsFontSelector = document.getElementById('windowsFontSelector');
  const scanWindowsFontsBtn = document.getElementById('scanWindowsFontsBtn');
  const customFontInput = document.getElementById('customFontInput');

  function applyFontToClock(fontFamilyName) {
    // Aplicar estilo inline al reloj para soportar cualquier fuente del sistema
    clockTime.style.fontFamily = `"${fontFamilyName}", sans-serif`;
    if (activeFontPreview) {
      activeFontPreview.textContent = `"${fontFamilyName}"`;
    }
  }

  windowsFontSelector.addEventListener('change', (e) => {
    applyFontToClock(e.target.value);
  });

  customFontInput.addEventListener('input', (e) => {
    const font = e.target.value.trim();
    if (font.length > 0) {
      applyFontToClock(font);
    }
  });

  // API queryLocalFonts (Edge / Chrome)
  scanWindowsFontsBtn.addEventListener('click', async () => {
    scanWindowsFontsBtn.textContent = '⏳ Escaneando fuentes de Windows...';

    if ('queryLocalFonts' in window) {
      try {
        const availableFonts = await window.queryLocalFonts();
        const uniqueFamilies = [...new Set(availableFonts.map(f => f.family))].sort();

        // Crear optgroup con las fuentes locales leídas de la PC
        const localOptGroup = document.createElement('optgroup');
        localOptGroup.label = `📁 Fuentes Detectadas en tu PC (${uniqueFamilies.length})`;

        uniqueFamilies.forEach(family => {
          const opt = document.createElement('option');
          opt.value = family;
          opt.textContent = family;
          localOptGroup.appendChild(opt);
        });

        windowsFontSelector.prepend(localOptGroup);
        windowsFontSelector.value = uniqueFamilies[0] || 'Segoe UI Variable';
        applyFontToClock(windowsFontSelector.value);

        scanWindowsFontsBtn.textContent = `✓ ${uniqueFamilies.length} Fuentes de Windows Listadas`;
        scanWindowsFontsBtn.style.background = '#10b981';
        if (cfgFontsCount) cfgFontsCount.textContent = `"${uniqueFamilies.length} fuentes de PC leídas"`;
      } catch (err) {
        console.warn('Permiso no concedido o cancelado:', err);
        fallbackWindowsFonts();
      }
    } else {
      fallbackWindowsFonts();
    }
  });

  function fallbackWindowsFonts() {
    const popularWindowsFonts = [
      'Segoe UI Variable', 'Segoe UI', 'Bahnschrift', 'Century Gothic',
      'Consolas', 'Cascadia Code', 'Trebuchet MS', 'Georgia', 'Arial Black',
      'Comic Sans MS', 'Impact', 'Verdana', 'Corbel', 'Calibri', 'Franklin Gothic Medium'
    ];

    const localOptGroup = document.createElement('optgroup');
    localOptGroup.label = '📁 Fuentes del Sistema Windows 11';

    popularWindowsFonts.forEach(family => {
      const opt = document.createElement('option');
      opt.value = family;
      opt.textContent = family;
      localOptGroup.appendChild(opt);
    });

    windowsFontSelector.prepend(localOptGroup);
    scanWindowsFontsBtn.textContent = '✓ Catálogo Windows 11 Cargado';
    if (cfgFontsCount) cfgFontsCount.textContent = '"Catálogo Windows 11 activo"';
  }

  // --- 7. MOVIMIENTO Y COORDENADAS DEL RELOJ ---
  const alignButtons = document.querySelectorAll('[data-align]');
  const alignPresets = {
    'top-left': { x: 28, y: 110 },
    'top-center': { x: 50, y: 140 },
    'top-right': { x: 72, y: 110 },
    'center': { x: 50, y: 260 }
  };

  function applyClockCoordinates(x, y) {
    depthClock.style.left = `${x}%`;
    depthClock.style.top = `${y}px`;
    depthClock.style.transform = `translateX(-${x}%)`;

    clockXRange.value = x;
    clockYRange.value = y;
    clockXLabel.textContent = `${x}%`;
    clockYLabel.textContent = `${y}px`;
  }

  alignButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      alignButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      const preset = alignPresets[btn.dataset.align];
      if (preset) applyClockCoordinates(preset.x, preset.y);
    });
  });

  clockYRange.addEventListener('input', (e) => {
    const val = e.target.value;
    depthClock.style.top = `${val}px`;
    clockYLabel.textContent = `${val}px`;
    alignButtons.forEach(b => b.classList.remove('active'));
  });

  clockXRange.addEventListener('input', (e) => {
    const val = e.target.value;
    depthClock.style.left = `${val}%`;
    depthClock.style.transform = `translateX(-${val}%)`;
    clockXLabel.textContent = `${val}%`;
    alignButtons.forEach(b => b.classList.remove('active'));
  });

  clockSizeRange.addEventListener('input', (e) => {
    const val = e.target.value;
    clockTime.style.fontSize = `${val}rem`;
    clockSizeLabel.textContent = `${val}rem`;
  });

  // --- 8. PERSONALIZAR WIDGETS ---
  const widgetChips = document.querySelectorAll('[data-widget]');
  widgetChips.forEach(chip => {
    chip.addEventListener('click', () => {
      chip.classList.toggle('active');
      const widgetId = chip.dataset.widget;
      const targetWidget = document.getElementById(widgetId);
      if (targetWidget) {
        targetWidget.classList.toggle('hidden', !chip.classList.contains('active'));
      }
    });
  });

  const colorDots = document.querySelectorAll('.color-dot');
  const accentHexColors = {
    cyan: '#00d2ff',
    purple: '#a855f7',
    green: '#10b981',
    orange: '#f97316'
  };

  colorDots.forEach(dot => {
    dot.addEventListener('click', () => {
      colorDots.forEach(d => d.classList.remove('active'));
      dot.classList.add('active');
      const color = accentHexColors[dot.dataset.accent] || '#00d2ff';
      document.documentElement.style.setProperty('--widget-accent', color);
    });
  });

  // --- 9. BARRA DE TAREAS: BÚSQUEDA Y CRISTAL ---
  const searchButtons = document.querySelectorAll('[data-search]');
  searchButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      searchButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      const mode = btn.dataset.search;
      streeckTaskbar.classList.remove('search-full', 'search-icon', 'search-hidden');
      streeckTaskbar.classList.add(`search-${mode}`);
    });
  });

  // --- 9. BARRA DE TAREAS Y MATERIALES AERO DWM GLOBALES ---
  const tbGlassButtons = document.querySelectorAll('[data-tb-glass]');
  const streeckStartMenu = document.getElementById('streeckStartMenu');
  const startBtn = document.getElementById('startBtn');
  const ctrlToggleStartMenu = document.getElementById('ctrlToggleStartMenu');
  const startPowerBtn = document.getElementById('startPowerBtn');
  const powerMenu = document.getElementById('powerMenu');
  const startSearchInput = document.getElementById('startSearchInput');

  tbGlassButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      tbGlassButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      const glass = btn.dataset.tbGlass;
      const glassClasses = ['glass-aero', 'glass-pure', 'glass-dark', 'glass-neon'];

      // 1. Aplica a la Barra de Tareas (3 islas o dock)
      if (streeckTaskbar) {
        streeckTaskbar.classList.remove(...glassClasses);
        streeckTaskbar.classList.add(glass);
      }

      // 2. Aplica a TODAS las Ventanas de aplicaciones (demostración con activeAppWindow)
      if (activeAppWindow) {
        activeAppWindow.classList.remove(...glassClasses);
        activeAppWindow.classList.add(glass);
      }

      // 3. Aplica al Menú de Inicio
      if (streeckStartMenu) {
        streeckStartMenu.classList.remove(...glassClasses);
        streeckStartMenu.classList.add(glass);
      }

      // Feedback háptico en Dynamic Island / Notch
      setNotchState('notif', `🎨 Estilo Aero DWM global aplicado: ${btn.textContent.trim()}`);
      setTimeout(() => setNotchState('idle'), 3000);
    });
  });

  // --- 9.5. INTERACCIONES DEL MENÚ DE INICIO AERO GLASS ---
  function toggleStartMenu(force) {
    if (!streeckStartMenu) return;
    const isCurrentlyOpen = streeckStartMenu.classList.contains('open');
    const shouldOpen = typeof force === 'boolean' ? force : !isCurrentlyOpen;

    if (shouldOpen) {
      streeckStartMenu.classList.add('open');
      if (startBtn) startBtn.classList.add('active');
      if (powerMenu) powerMenu.classList.remove('open');
      if (startSearchInput) setTimeout(() => startSearchInput.focus(), 140);
      setNotchState('notif', '🪟 Menú de Inicio Aero desplegado');
      setTimeout(() => setNotchState('idle'), 2400);
    } else {
      streeckStartMenu.classList.remove('open');
      if (startBtn) startBtn.classList.remove('active');
      if (powerMenu) powerMenu.classList.remove('open');
    }
  }

  // Clic en el botón Inicio de la barra (Isla 1)
  if (startBtn) {
    startBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleStartMenu();
    });
  }

  // Clic en el botón de acceso directo del panel de control
  if (ctrlToggleStartMenu) {
    ctrlToggleStartMenu.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleStartMenu();
    });
  }

  // Menú de opciones de energía / apagado
  if (startPowerBtn && powerMenu) {
    startPowerBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      powerMenu.classList.toggle('open');
    });
  }

  // Acciones de apagado / suspensión
  const powerItems = document.querySelectorAll('.power-menu-item');
  powerItems.forEach(item => {
    item.addEventListener('click', (e) => {
      e.stopPropagation();
      const action = item.dataset.power;
      const labels = {
        sleep: '🌙 Suspendiendo equipo y ahorrando energía...',
        restart: '🔄 Reiniciando sistema operativo Windows 11...',
        shutdown: '⏻ Apagando el equipo...',
        lock: '🔒 Sesión bloqueada con seguridad DWM Aero'
      };
      setNotchState('notif', labels[action] || 'Acción de energía');
      if (powerMenu) powerMenu.classList.remove('open');
      toggleStartMenu(false);
      setTimeout(() => setNotchState('idle'), 3500);
    });
  });

  // Clics en aplicaciones ancladas del Menú de Inicio
  const pinnedApps = document.querySelectorAll('.pinned-app-item');
  pinnedApps.forEach(item => {
    item.addEventListener('click', (e) => {
      e.stopPropagation();
      const appName = item.dataset.appName || item.querySelector('.pinned-name').textContent;

      // Si hace clic en VS Code, restaurar o resaltar la ventana interactiva
      if (appName.includes('VS Code') || appName.includes('Code')) {
        if (isWindowMinimized) {
          toggleMinimizeWindow();
        } else {
          activeAppWindow.style.transform = 'translate(-50%, -50%) scale(1.025)';
          setTimeout(() => {
            activeAppWindow.style.transform = 'translate(-50%, -50%) scale(1)';
          }, 200);
        }
      }

      setNotchState('notif', `🚀 Iniciando ${appName} con aceleración Aero DWM...`);
      toggleStartMenu(false);
      setTimeout(() => setNotchState('idle'), 3000);
    });
  });

  // Cerrar Menú de Inicio al hacer clic fuera
  document.addEventListener('click', (e) => {
    if (streeckStartMenu && streeckStartMenu.classList.contains('open')) {
      if (!streeckStartMenu.contains(e.target) && !startBtn.contains(e.target) && !(ctrlToggleStartMenu && ctrlToggleStartMenu.contains(e.target))) {
        toggleStartMenu(false);
      }
    }
  });

  // Tecla Escape para cerrar Menú de Inicio
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      if (streeckStartMenu && streeckStartMenu.classList.contains('open')) {
        toggleStartMenu(false);
      }
    }
  });

  // --- 10. WALLPAPERS 3D DEPTH ---
  const wallpapers = [
    {
      name: "Cyberpunk Horizon",
      bg: "radial-gradient(ellipse at 50% 80%, #312e81 0%, #0f172a 60%, #030712 100%)",
      subjectSvg: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1200 800' preserveAspectRatio='xMidYMax slice'><defs><linearGradient id='neonG' x1='0' y1='0' x2='0' y2='1'><stop offset='0%' stop-color='%231e1b4b'/><stop offset='100%' stop-color='%23090d16'/></linearGradient></defs><path d='M0,800 L0,550 L120,550 L120,480 L220,480 L220,530 L340,530 L340,380 L440,380 L440,320 L500,280 L560,320 L560,420 L660,420 L660,260 L720,220 L780,260 L780,450 L890,450 L890,360 L980,360 L980,490 L1080,490 L1080,540 L1200,540 L1200,800 Z' fill='url(%23neonG)'/><path d='M480,280 L520,280 L520,800 L480,800 Z' fill='%230f172a'/><circle cx='600' cy='310' r='120' fill='%236366f1' opacity='0.18'/></svg>")`
    },
    {
      name: "Monte Sereno / Cordillera",
      bg: "linear-gradient(180deg, #1e1b4b 0%, #4338ca 40%, #f97316 85%, #fbbf24 100%)",
      subjectSvg: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1200 800' preserveAspectRatio='xMidYMax slice'><polygon points='100,800 450,260 620,480 820,220 1150,800' fill='%230f172a'/><polygon points='750,800 820,220 900,340 1050,800' fill='%231e293b'/><polygon points='380,800 450,260 520,380 700,800' fill='%231e293b'/></svg>")`
    },
    {
      name: "Arquitectura Minimalista",
      bg: "radial-gradient(circle at 60% 30%, #1e293b 0%, #0b0f19 80%)",
      subjectSvg: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1200 800' preserveAspectRatio='xMidYMax slice'><path d='M250,800 C250,300 950,300 950,800 Z' fill='%23111827' stroke='%2338bdf8' stroke-width='4' stroke-opacity='0.4'/><ellipse cx='600' cy='450' rx='140' ry='180' fill='%23030712'/></svg>")`
    }
  ];

  let is3DExploded = false;

  // El módulo depth_wallpaper aplica las capas; app.js solo le entrega los presets y refleja su estado
  if (window.DepthWallpaperModule) {
    window.DepthWallpaperModule.setPresets(wallpapers);
  }

  changeWallpaperBtn.addEventListener('click', () => {
    bus.emit('wallpaper:cycle');
  });

  toggleDepthBtn.addEventListener('click', () => {
    bus.emit('wallpaper:toggle_depth');
  });

  bus.on('wallpaper:depth_toggled', ({ enabled }) => {
    toggleDepthBtn.textContent = enabled ? 'Silueta 3D: ON' : 'Silueta 3D: OFF';
    toggleDepthBtn.classList.toggle('active', enabled);
  });

  explode3DBtn.addEventListener('click', () => {
    is3DExploded = !is3DExploded;
    wallpaperSystem.classList.toggle('exploded', is3DExploded);
    explode3DBtn.textContent = is3DExploded ? '✓ Volver a Normal' : '👁️ Desglose de Capas 3D';
    explode3DBtn.classList.toggle('active', is3DExploded);
  });

  // --- 11. RELOJ EN TIEMPO REAL ---
  function updateClock() {
    const now = new Date();
    const hours = String(now.getHours()).padStart(2, '0');
    const minutes = String(now.getMinutes()).padStart(2, '0');
    clockTime.textContent = `${hours}:${minutes}`;

    const options = { weekday: 'long', day: 'numeric', month: 'long' };
    clockDate.textContent = now.toLocaleDateString('es-ES', options).toUpperCase();

    const trayTime = document.querySelector('.tray-time');
    const trayDate = document.querySelector('.tray-date');
    if (trayTime) trayTime.textContent = `${hours}:${minutes}`;
    if (trayDate) trayDate.textContent = `${now.getDate()}/${now.getMonth() + 1}/${now.getFullYear()}`;
  }

  setInterval(updateClock, 1000);
  updateClock();

  // --- 12. NOTCH DINÁMICO ---
  const notchButtons = document.querySelectorAll('[data-notch]');

  const notifSender = document.querySelector('#notchNotification .notif-sender');
  const notifMsg = document.querySelector('#notchNotification .notif-msg');

  function setNotchState(state, message) {
    if (state === 'notif' && message) {
      if (notifSender) notifSender.textContent = 'Windows 11 Suite';
      if (notifMsg) notifMsg.textContent = message;
    }
    dynamicNotch.setAttribute('data-state', state);
    notchButtons.forEach(btn => btn.classList.toggle('active', btn.dataset.notch === state));

    if (state === 'ai') {
      simulateVoiceAIResponse();
    }
  }

  notchButtons.forEach(btn => {
    btn.addEventListener('click', () => setNotchState(btn.dataset.notch));
  });

  dynamicNotch.addEventListener('click', (e) => {
    if (e.target.closest('.media-controls') || e.target.closest('.action-btn-reply')) return;
    const currentState = dynamicNotch.getAttribute('data-state');
    if (currentState === 'idle') setNotchState('ai');
    else if (currentState === 'ai') setNotchState('music');
    else if (currentState === 'music') setNotchState('notif');
    else setNotchState('idle');
  });

  let isPlaying = true;
  playPauseBtn.addEventListener('click', (e) => {
    e.stopPropagation();
    isPlaying = !isPlaying;
    playPauseBtn.textContent = isPlaying ? '⏸' : '▶';
    const wave = document.querySelector('.audio-bars-wave');
    if (wave) wave.style.opacity = isPlaying ? '1' : '0.3';
  });

  const aiPhrases = [
    "Escuchando tu voz... Di: 'minimizar ventana' o 'dividir barra en 3 islas'.",
    "Comprendido. Ajustando el modo privacidad Aero Glass.",
    "Buscando fuentes instaladas en tu sistema Windows... Todo listo.",
    "Rendimiento del sistema óptimo y fluido a 60/120 FPS."
  ];

  let phraseIdx = 0;
  function simulateVoiceAIResponse() {
    aiTranscript.textContent = "Escuchando tu voz...";
    setTimeout(() => {
      phraseIdx = (phraseIdx + 1) % aiPhrases.length;
      aiTranscript.textContent = `"${aiPhrases[phraseIdx]}"`;
    }, 1200);
  }

  // --- 13. MODO AERO PRIVACY / FOCUS BLUR ---
  // El módulo aero_privacy es quien aplica el desenfoque; aquí solo se refleja su estado en los controles
  function toggleAeroPrivacy() {
    bus.emit('privacy:toggle');
  }

  bus.on('privacy:state_changed', ({ active: isAeroPrivacyActive }) => {
    if (toggleAeroBtn) {
      toggleAeroBtn.classList.toggle('on', isAeroPrivacyActive);
      toggleAeroBtn.textContent = isAeroPrivacyActive ? 'DESACTIVAR' : 'ACTIVAR';
    }

    if (quickAeroToggle) quickAeroToggle.classList.toggle('active', isAeroPrivacyActive);
    if (quickAeroStatus) quickAeroStatus.textContent = isAeroPrivacyActive ? 'ON' : 'OFF';

    if (trayAeroBtn) trayAeroBtn.style.color = isAeroPrivacyActive ? '#00d2ff' : '';
  });

  if (toggleAeroBtn) toggleAeroBtn.addEventListener('click', toggleAeroPrivacy);
  if (trayAeroBtn) trayAeroBtn.addEventListener('click', toggleAeroPrivacy);
  if (quickAeroToggle) quickAeroToggle.addEventListener('click', toggleAeroPrivacy);
  if (aeroBackdrop) aeroBackdrop.addEventListener('click', toggleAeroPrivacy);

  // Botón minimizar panel
  togglePanelBtn.addEventListener('click', () => {
    controlPanel.classList.toggle('minimized');
    togglePanelBtn.textContent = controlPanel.classList.contains('minimized') ? '+' : '−';
  });

  // Apps en la barra de tareas
  const appButtons = document.querySelectorAll('.app-icon-btn:not(#taskbarVsCodeBtn)');
  appButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      appButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
    });
  });

  // --- 14. TELEMETRÍA EN VIVO ---
  setInterval(() => {
    const cpu = Math.floor(14 + Math.random() * 12);
    const ram = Math.floor(41 + Math.random() * 4);
    const gpu = Math.floor(22 + Math.random() * 15);

    if (cpuVal) cpuVal.textContent = `${cpu}%`;
    if (ramVal) ramVal.textContent = `${ram}%`;
    if (gpuVal) gpuVal.textContent = `${gpu}%`;

    if (cpuBar) cpuBar.style.width = `${cpu}%`;
    if (ramBar) ramBar.style.width = `${ram}%`;
    if (gpuBar) gpuBar.style.width = `${gpu}%`;
  }, 2500);

});
