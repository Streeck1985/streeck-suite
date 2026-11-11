// streeck_taskbar.dll: se carga dentro de explorer.exe y le da el skin de islas a la barra de tareas REAL de Windows 11.
//
// Cómo entra: la suite pone un gancho de ventana (SetWindowsHookEx) en el hilo de la barra; Windows carga esta DLL
// en el Explorador, la DLL se fija para no descargarse y pide a Windows una sesión de diagnóstico XAML
// (InitializeXamlDiagnosticsEx, la API oficial que usa Visual Studio). Windows crea entonces nuestro objeto TAP y
// le entrega el árbol visual de la barra. Mismo mecanismo que TranslucentTB (ExplorerTAP) y el Taskbar Styler de
// Windhawk, escrito desde cero.
//
// Qué hace (mockup I3): apaga el fondo de Windows, dibuja detrás de los iconos el fondo de cada isla (clima, inicio +
// búsqueda + apps, bandeja + reloj) y, en los formatos centrados, acerca las piezas reales con Translation. Los
// iconos, menús, arrastrar y ⌃ siguen siendo los de Windows.
//
// La suite le habla por archivos y un evento con nombre:
//   %LOCALAPPDATA%\StreeckSuite\barra_skin.ini   activo, formato, separacion, extremos, cristal, arbol
//   Local\StreeckSuite.TaskbarSkin               evento: vuelve a leer los ajustes y los aplica
//   barra_skin.log / barra_arbol.txt             registro y volcado del árbol de piezas

#define NOMINMAX
#include <functional>
#include <windows.h>
#include <unknwn.h>
#include <ocidl.h>
#include <xamlom.h>
#undef GetCurrentTime // Choca con Storyboard.GetCurrentTime de C++/WinRT
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Windows.UI.Xaml.Media.Animation.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.Media.Control.h>
#include <winrt/Windows.Storage.Streams.h>
#include <shlwapi.h>
#include <shcore.h>
#include <cmath>
#include <mutex>
#include <algorithm>
#include <cstdio>
#include <fstream>
#include <map>
#include <memory>
#include <string>
#include <thread>
#include <vector>

namespace wux = winrt::Windows::UI::Xaml;
namespace wuxc = winrt::Windows::UI::Xaml::Controls;
namespace wuxm = winrt::Windows::UI::Xaml::Media;
namespace wuxs = winrt::Windows::UI::Xaml::Shapes;
using winrt::Windows::Foundation::Rect;
using winrt::Windows::UI::Color;

// {E3A1B6C2-5D47-4F2A-9C3E-7B1D2F8A6E41}
static const CLSID CLSID_StreeckTap = { 0xe3a1b6c2, 0x5d47, 0x4f2a, { 0x9c, 0x3e, 0x7b, 0x1d, 0x2f, 0x8a, 0x6e, 0x41 } };

static HMODULE g_module;
static winrt::com_ptr<IXamlDiagnostics> g_diagnostics;

struct Node
{
    std::wstring Type, Name;
    InstanceHandle Parent;
};

// Ajustes del panel (mismos valores que SuiteSettings)
enum Format { Islands = 0, Capsule = 1, Full = 2 };
enum Glass { Aero = 0, Transparent = 1, Obsidian = 2, Neon = 3 };
struct Settings
{
    bool Active = false;
    int Format = Islands;
    int Gap = 14;
    bool Edges = false;
    int Glass = Aero;
    bool MiniPlayer = false;  // 4.ª isla con la música (sugerencia aprobada del mockup de la barra)
    Color IslandColor{ 219, 0, 0, 0 };  // Mismo color y opacidad que la isla de arriba (para el skin Notch)
    int Skin = 1;                       // Skin de la barra (ver enum Skin); Notch por defecto
    // Icono del botón de Inicio: 0 Windows original, 3 ojitos (dibujados aquí); el resto es la imagen inicio_archivo
    int StartIcon = 0;
    int StartSize = 100;                // 70..140 %
    int StartHover = 1;                 // 0 nada, 1 crece, 2 gira
    std::wstring StartFile;
    std::wstring StartClosedFile;       // Ojitos: la misma cara con los ojos cerrados (para parpadear)
    std::wstring StartFacesBase;        // Ojitos: base de las caras (base_n.png, base_f.png...): imitan a la isla
    bool StartOpensSuite = true;        // El clic en Inicio abre el menú de Streeck (no el de Windows)
    int Edge = 3;                       // Borde de la barra: 0 izquierda, 1 arriba, 2 derecha, 3 abajo
    int Line = 0;                       // Contorno (Ajustes › Apariencia): 0 el del skin, 1 sin borde, 2 fino, 3 brillo
    bool Frosted = false;               // Fondo cristal: el skin más transparente
    bool Rounded = false;               // Forma redondeada: esquinas suaves en lugar de píldora
};

// Una barra (la principal y una por cada monitor extra)
struct FrameState
{
    winrt::weak_ref<wux::FrameworkElement> Frame;
    winrt::weak_ref<wuxc::Grid> Root;
    winrt::weak_ref<wux::FrameworkElement> Repeater;
    winrt::weak_ref<wuxc::Canvas> Canvas;
    std::wstring Drawn;  // Firma de lo dibujado: solo se redibuja si cambia (evita bucles con LayoutUpdated)
    double GroupShift[3] = { 0, 0, 0 };
    // Medidas con la barra a su tamaño normal (se toman con el marco sin estrechar y se reusan)
    bool Calibrated = false;
    size_t CalibratedCount = 0;
    double Side = 0, Apps = 0, TrayInset = 0, TrayWidth = 0;
    double Slack = 16;
    bool Hooked = false;
    bool Pending = false;
    // Icono de Inicio de esta barra
    winrt::weak_ref<wux::FrameworkElement> StartButton, StartHookedButton;
    winrt::weak_ref<wuxc::Border> StartOverlay;  // Capa invisible sobre el botón: el clic abre el menú de Streeck
    std::wstring StartDrawn;
    bool Diagnosed = false;  // Ya se guardó el árbol porque faltaba una pieza
    bool TraySiblingLogged = false;  // Ya se avisó en el registro que la bandeja está al lado del marco
    wux::DispatcherTimer Recheck{ nullptr };  // Vuelve a medir por si un icono terminó de animarse sin avisar
    int Burst = 0;      // Tras un cambio en la barra: mediciones rápidas (cada 250 ms) mientras los iconos se animan
    int Idle = 0;       // Sin cambios: una medición cada 12 vueltas (3 s)
    int Pieces = -1;    // Piezas de la barra la última vez (si cambia, se abrió o cerró una app)
};

// Todo esto solo se toca desde el hilo de la interfaz de la barra (los avisos del árbol llegan ahí)
static std::map<InstanceHandle, Node> g_nodes;
static std::map<InstanceHandle, double> g_originalOpacity;   // Fondo y borde de Windows con su opacidad original
static std::map<void*, float> g_shift;                        // Desplazamiento que le pusimos a cada pieza
static std::vector<std::shared_ptr<FrameState>> g_frames;
static winrt::weak_ref<wux::UIElement> g_anchor;             // Cualquier pieza de la barra, para llegar a su hilo
static Settings g_settings;

#pragma region Utilidades

static std::wstring DataFolder()
{
    wchar_t buffer[MAX_PATH];
    DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", buffer, MAX_PATH);
    return std::wstring(buffer, length) + L"\\StreeckSuite";
}

static void Log(const std::wstring& text)
{
    SYSTEMTIME now;
    GetLocalTime(&now);
    wchar_t stamp[32];
    swprintf_s(stamp, L"%02d:%02d:%02d.%03d ", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
    std::wofstream file(DataFolder() + L"\\barra_skin.log", std::ios::app);
    file << stamp << text << L"\n";
}

static std::map<std::wstring, std::wstring> ReadIni()
{
    std::map<std::wstring, std::wstring> values;
    std::wifstream file(DataFolder() + L"\\barra_skin.ini");
    std::wstring line;
    while (std::getline(file, line))
    {
        size_t equals = line.find(L'=');
        if (equals == std::wstring::npos) continue;
        std::wstring value = line.substr(equals + 1);
        while (!value.empty() && (value.back() == L'\r' || value.back() == L' ')) value.pop_back();
        values[line.substr(0, equals)] = value;
    }
    return values;
}

static int ToInt(const std::map<std::wstring, std::wstring>& values, const wchar_t* key, int fallback)
{
    auto it = values.find(key);
    if (it == values.end() || it->second.empty()) return fallback;
    try { return std::stoi(it->second); } catch (...) { return fallback; }
}

static void LoadSettings()
{
    auto values = ReadIni();
    g_settings.Active = ToInt(values, L"activo", 0) == 1;
    g_settings.Format = std::clamp(ToInt(values, L"formato", Islands), 0, 2);
    g_settings.Gap = std::clamp(ToInt(values, L"separacion", 14), 0, 80);
    g_settings.Edges = ToInt(values, L"extremos", 0) == 1;
    g_settings.Glass = std::clamp(ToInt(values, L"cristal", Aero), 0, 3);
    g_settings.MiniPlayer = ToInt(values, L"miniplayer", 0) == 1;
    g_settings.Skin = std::clamp(ToInt(values, L"skin", 1), 0, 15);
    g_settings.Line = std::clamp(ToInt(values, L"linea", 0), 0, 3);
    g_settings.Frosted = ToInt(values, L"vidrio", 0) == 1;
    g_settings.Rounded = ToInt(values, L"redondeada", 0) == 1;
    g_settings.StartIcon = std::clamp(ToInt(values, L"inicio_icono", 0), 0, 6);
    g_settings.StartSize = std::clamp(ToInt(values, L"inicio_tamano", 100), 70, 140);
    g_settings.StartHover = std::clamp(ToInt(values, L"inicio_efecto", 1), 0, 2);
    auto startFile = values.find(L"inicio_archivo");
    g_settings.StartFile = startFile != values.end() ? startFile->second : L"";
    auto closedFile = values.find(L"inicio_cerrados");
    g_settings.StartClosedFile = closedFile != values.end() ? closedFile->second : L"";
    auto facesBase = values.find(L"inicio_caras");
    g_settings.StartFacesBase = facesBase != values.end() ? facesBase->second : L"";
    g_settings.StartOpensSuite = ToInt(values, L"inicio_clic", 1) == 1;
    g_settings.Edge = std::clamp(ToInt(values, L"borde", 3), 0, 3);
    // Color "#RRGGBB" y opacidad 30..100 de la isla de arriba
    int opacity = std::clamp(ToInt(values, L"opacidad", 86), 30, 100);
    auto color = values.find(L"color");
    unsigned int rgb = 0;
    if (color != values.end() && color->second.size() == 7 && color->second[0] == L'#')
    {
        try { rgb = std::stoul(color->second.substr(1), nullptr, 16); } catch (...) { rgb = 0; }
    }
    g_settings.IslandColor = Color{ (uint8_t)(opacity * 255 / 100), (uint8_t)(rgb >> 16), (uint8_t)((rgb >> 8) & 0xFF), (uint8_t)(rgb & 0xFF) };
}

static wux::UIElement ElementFrom(InstanceHandle handle)
{
    winrt::Windows::Foundation::IInspectable object;
    if (!g_diagnostics || FAILED(g_diagnostics->GetIInspectableFromHandle(handle, reinterpret_cast<::IInspectable**>(winrt::put_abi(object))))) return nullptr;
    return object.try_as<wux::UIElement>();
}

// ¿Está la pieza dentro de una barra (Taskbar.TaskbarFrame)?
static bool InsideTaskbarFrame(InstanceHandle handle)
{
    for (int depth = 0; depth < 64; depth++)
    {
        auto it = g_nodes.find(handle);
        if (it == g_nodes.end()) return false;
        if (it->second.Type == L"Taskbar.TaskbarFrame") return true;
        handle = it->second.Parent;
    }
    return false;
}

static wux::FrameworkElement FindByName(wux::DependencyObject const& root, std::wstring_view name, int depth = 0)
{
    if (!root || depth > 10) return nullptr;
    int count = wuxm::VisualTreeHelper::GetChildrenCount(root);
    for (int i = 0; i < count; i++)
    {
        wux::DependencyObject child = wuxm::VisualTreeHelper::GetChild(root, i);
        wux::FrameworkElement element = child.try_as<wux::FrameworkElement>();
        if (element && element.Name() == name) return element;
        wux::FrameworkElement found = FindByName(child, name, depth + 1);
        if (found) return found;
    }
    return nullptr;
}

static wux::FrameworkElement FindByClass(wux::DependencyObject const& root, std::wstring_view className, int depth = 0)
{
    if (!root || depth > 30) return nullptr;
    int count = wuxm::VisualTreeHelper::GetChildrenCount(root);
    for (int i = 0; i < count; i++)
    {
        wux::DependencyObject child = wuxm::VisualTreeHelper::GetChild(root, i);
        if (winrt::get_class_name(child) == className) return child.try_as<wux::FrameworkElement>();
        wux::FrameworkElement found = FindByClass(child, className, depth + 1);
        if (found) return found;
    }
    return nullptr;
}

static void DumpTree(const wchar_t* fileName = L"barra_arbol.txt");

static bool HasDescendantClass(wux::DependencyObject const& root, std::wstring_view className, int depth = 0)
{
    if (!root || depth > 6) return false;
    int count = wuxm::VisualTreeHelper::GetChildrenCount(root);
    for (int i = 0; i < count; i++)
    {
        wux::DependencyObject child = wuxm::VisualTreeHelper::GetChild(root, i);
        if (winrt::get_class_name(child) == className) return true;
        if (HasDescendantClass(child, className, depth + 1)) return true;
    }
    return false;
}

// Borde izquierdo y derecho de lo que se ve dentro de una pieza (textos, imágenes y animaciones)
// Windows anima el clima con Translation, que TransformToVisual no cuenta: se suma a mano por el camino
static void VisibleExtent(wux::DependencyObject const& node, wux::UIElement const& relativeTo, double& left, double& right, double translation = 0, int depth = 0)
{
    if (depth > 12) return;
    int count = wuxm::VisualTreeHelper::GetChildrenCount(node);
    for (int i = 0; i < count; i++)
    {
        wux::FrameworkElement child = wuxm::VisualTreeHelper::GetChild(node, i).try_as<wux::FrameworkElement>();
        if (!child || child.Visibility() != wux::Visibility::Visible || child.Opacity() <= 0) continue;
        double moved = translation + child.Translation().x;
        std::wstring className(winrt::get_class_name(child));
        bool leaf = className == L"Windows.UI.Xaml.Controls.TextBlock" || className == L"Windows.UI.Xaml.Controls.Image" ||
            className == L"Microsoft.UI.Xaml.Controls.AnimatedVisualPlayer";
        if (leaf && child.ActualWidth() > 1)
        {
            Rect r = child.TransformToVisual(relativeTo).TransformBounds(Rect{ 0, 0, (float)child.ActualWidth(), (float)child.ActualHeight() });
            left = std::min(left, r.X + moved);
            right = std::max(right, r.X + r.Width + moved);
        }
        else
        {
            VisibleExtent(child, relativeTo, left, right, moved, depth + 1);
        }
    }
}

static Color Argb(uint8_t a, uint8_t r, uint8_t g, uint8_t b)
{
    return Color{ a, r, g, b };
}

#pragma endregion

#pragma region Dibujo de las islas

// Skins de la barra (galería aprobada por el usuario): Cristal, Notch, Dock, Neón, Fantasma, Línea fina, Aurora,
// Aero 7, Cristal líquido y Gamer RGB. Grupo: 0 clima, 1 apps, 2 bandeja, 3 mini reproductor.
enum Skin { SkinGlass = 0, SkinNotch, SkinDock, SkinNeon, SkinGhost, SkinLine, SkinAurora, SkinAero7, SkinLiquid, SkinRgb,
            SkinGraphite, SkinPaper, SkinSunset, SkinTerminal, SkinIce, SkinGold };

static std::vector<wuxm::Animation::Storyboard> g_animations;  // Bordes RGB en marcha (se paran al redibujar)

static wuxm::LinearGradientBrush Gradient(std::initializer_list<std::pair<double, Color>> stops, bool vertical)
{
    wuxm::LinearGradientBrush brush;
    brush.StartPoint(winrt::Windows::Foundation::Point{ 0, 0 });
    brush.EndPoint(vertical ? winrt::Windows::Foundation::Point{ 0, 1 } : winrt::Windows::Foundation::Point{ 1, 1 });
    for (auto& stop : stops)
    {
        wuxm::GradientStop s;
        s.Offset(stop.first);
        s.Color(stop.second);
        brush.GradientStops().Append(s);
    }
    return brush;
}

// Degradado que gira: la animación la hace el compositor de Windows, casi sin gasto
static wuxm::Brush SpinningBrush(wuxm::LinearGradientBrush brush);

// Arcoíris que gira (Gamer RGB)
static wuxm::Brush RainbowBrush()
{
    return SpinningBrush(Gradient({ { 0.0, Argb(255, 255, 0, 76) }, { 0.2, Argb(255, 255, 184, 0) }, { 0.4, Argb(255, 61, 255, 110) },
        { 0.6, Argb(255, 0, 194, 255) }, { 0.8, Argb(255, 157, 77, 255) }, { 1.0, Argb(255, 255, 0, 76) } }, false));
}

// Brillo de un color que recorre el contorno (Oro y el borde «Brillo»)
static wuxm::Brush GlowBrush(Color main)
{
    Color light = Argb(255, (uint8_t)std::min(255, main.R + 90), (uint8_t)std::min(255, main.G + 90), (uint8_t)std::min(255, main.B + 90));
    return SpinningBrush(Gradient({ { 0.0, Color{ 90, main.R, main.G, main.B } }, { 0.35, light }, { 0.65, main }, { 1.0, Color{ 90, main.R, main.G, main.B } } }, false));
}

static wuxm::Brush SpinningBrush(wuxm::LinearGradientBrush brush)
{
    wuxm::RotateTransform rotate;
    rotate.CenterX(0.5);
    rotate.CenterY(0.5);
    brush.RelativeTransform(rotate);
    wuxm::Animation::DoubleAnimation spin;
    spin.From(0.0);
    spin.To(360.0);
    spin.Duration(wux::DurationHelper::FromTimeSpan(std::chrono::seconds(4)));
    spin.RepeatBehavior(wuxm::Animation::RepeatBehaviorHelper::Forever());
    wuxm::Animation::Storyboard story;
    wuxm::Animation::Storyboard::SetTarget(spin, rotate);
    wuxm::Animation::Storyboard::SetTargetProperty(spin, L"Angle");
    story.Children().Append(spin);
    story.Begin();
    g_animations.push_back(story);
    return brush;
}

// Relleno de cada skin (el mini reproductor también lo usa)
static wuxm::Brush IslandFill(int group)
{
    switch (g_settings.Skin)
    {
        case SkinNotch: return wuxm::SolidColorBrush(g_settings.IslandColor);
        case SkinDock: return wuxm::SolidColorBrush(Argb(150, 28, 30, 38));
        case SkinNeon: case SkinRgb: return wuxm::SolidColorBrush(Argb(225, 8, 10, 20));
        case SkinGhost: return wuxm::SolidColorBrush(Color{ 0, 0, 0, 0 });
        case SkinLine: return wuxm::SolidColorBrush(Argb(225, 10, 12, 18));
        case SkinAurora:
            return group % 2 == 1 ? Gradient({ { 0, Argb(220, 124, 58, 237) }, { 1, Argb(220, 236, 72, 153) } }, false)
                                  : Gradient({ { 0, Argb(230, 30, 41, 59) }, { 1, Argb(220, 76, 29, 149) } }, false);
        case SkinAero7:
            return Gradient({ { 0, Argb(97, 255, 255, 255) }, { 0.48, Argb(20, 255, 255, 255) }, { 0.52, Argb(140, 20, 40, 80) }, { 1, Argb(190, 20, 40, 80) } }, true);
        case SkinLiquid: return wuxm::SolidColorBrush(Argb(26, 255, 255, 255));
        case SkinGraphite: return wuxm::SolidColorBrush(Argb(235, 42, 44, 49));
        case SkinPaper: return wuxm::SolidColorBrush(Argb(240, 250, 250, 248));
        case SkinSunset: return Gradient({ { 0, Argb(235, 255, 122, 69) }, { 0.55, Argb(235, 255, 61, 119) }, { 1, Argb(235, 122, 44, 255) } }, false);
        case SkinTerminal: return wuxm::SolidColorBrush(Argb(240, 2, 7, 2));
        case SkinIce: return wuxm::SolidColorBrush(Argb(190, 214, 236, 255));
        case SkinGold: return wuxm::SolidColorBrush(Argb(240, 10, 10, 10));
        default: return wuxm::SolidColorBrush(Argb(210, 14, 18, 28)); // Cristal
    }
}

// Color propio del skin para el borde «Brillo»
static Color SkinAccent()
{
    switch (g_settings.Skin)
    {
        case SkinTerminal: return Argb(255, 77, 255, 122);
        case SkinGold: return Argb(255, 232, 196, 106);
        case SkinPaper: return Argb(255, 37, 99, 235);
        case SkinIce: return Argb(255, 14, 165, 233);
        case SkinGraphite: return Argb(255, 161, 161, 170);
        default: return Argb(255, 56, 189, 248);
    }
}

static bool LightSkin() { return g_settings.Skin == SkinPaper || g_settings.Skin == SkinIce; }

static wuxm::Brush IslandStroke(int group)
{
    if (g_settings.Line == 2) return wuxm::SolidColorBrush(LightSkin() ? Argb(40, 0, 0, 0) : Argb(46, 255, 255, 255));
    if (g_settings.Line == 3) return GlowBrush(SkinAccent());
    switch (g_settings.Skin)
    {
        case SkinGraphite: return wuxm::SolidColorBrush(Argb(26, 255, 255, 255));
        case SkinPaper: return wuxm::SolidColorBrush(Argb(30, 0, 0, 0));
        case SkinTerminal: return wuxm::SolidColorBrush(Argb(90, 77, 255, 122));
        case SkinIce: return wuxm::SolidColorBrush(Argb(150, 255, 255, 255));
        case SkinGold: return GlowBrush(SkinAccent());
        case SkinGlass: return wuxm::SolidColorBrush(Argb(41, 255, 255, 255));
        case SkinNeon: return wuxm::SolidColorBrush(group == 3 ? Argb(220, 244, 114, 182) : Argb(220, 56, 189, 248));
        case SkinAero7: return wuxm::SolidColorBrush(Argb(140, 255, 255, 255));
        case SkinLiquid: return wuxm::SolidColorBrush(Argb(72, 255, 255, 255));
        case SkinDock: return wuxm::SolidColorBrush(Argb(50, 255, 255, 255));
        case SkinRgb: return RainbowBrush();
        default: return wuxm::SolidColorBrush(Color{ 0, 0, 0, 0 });
    }
}

static double StrokeWidth()
{
    if (g_settings.Line == 1) return 0;
    if (g_settings.Line == 2) return 1;
    if (g_settings.Line == 3) return 1.5;
    switch (g_settings.Skin)
    {
        case SkinGold: return 1.5;
        case SkinGraphite: case SkinPaper: case SkinTerminal: case SkinIce: return 1;
        case SkinNeon: return 1.5;
        case SkinRgb: return 2;
        case SkinGlass: case SkinAero7: case SkinLiquid: case SkinDock: return 1;
        default: return 0;
    }
}

static double IslandRadius(double height)
{
    if (g_settings.Rounded) return std::min(10.0, height / 2);
    return g_settings.Skin == SkinAero7 ? 10 : height / 2;
}

static void AddRect(wuxc::Canvas const& canvas, double x, double y, double w, double h, double radius, Color fill, Color stroke, double thickness)
{
    wuxs::Rectangle rect;
    rect.Width(std::max(0.0, w));
    rect.Height(std::max(0.0, h));
    rect.RadiusX(radius);
    rect.RadiusY(radius);
    rect.Fill(wuxm::SolidColorBrush(fill));
    if (thickness > 0)
    {
        rect.Stroke(wuxm::SolidColorBrush(stroke));
        rect.StrokeThickness(thickness);
    }
    wuxc::Canvas::SetLeft(rect, x);
    wuxc::Canvas::SetTop(rect, y);
    canvas.Children().Append(rect);
}

// Notch: pegada al borde de abajo, esquinas de arriba redondeadas y orejitas cóncavas abajo (espejo del notch de arriba)
static void AddNotchShape(wuxc::Canvas const& canvas, double x, double y, double w, double bottom, double radius, wuxm::Brush const& fill)
{
    using winrt::Windows::Foundation::Point;
    using winrt::Windows::Foundation::Size;
    double ear = 9, r = std::min(radius, (bottom - y) / 2);
    wuxm::PathFigure figure;
    figure.StartPoint(Point{ (float)(x - ear), (float)bottom });
    figure.IsClosed(true);
    auto arc = [&](double px, double py, double size, wuxm::SweepDirection dir)
    {
        wuxm::ArcSegment segment;
        segment.Point(Point{ (float)px, (float)py });
        segment.Size(Size{ (float)size, (float)size });
        segment.SweepDirection(dir);
        figure.Segments().Append(segment);
    };
    auto line = [&](double px, double py)
    {
        wuxm::LineSegment segment;
        segment.Point(Point{ (float)px, (float)py });
        figure.Segments().Append(segment);
    };
    arc(x, bottom - ear, ear, wuxm::SweepDirection::Counterclockwise);
    line(x, y + r);
    arc(x + r, y, r, wuxm::SweepDirection::Clockwise);
    line(x + w - r, y);
    arc(x + w, y + r, r, wuxm::SweepDirection::Clockwise);
    line(x + w, bottom - ear);
    arc(x + w + ear, bottom, ear, wuxm::SweepDirection::Counterclockwise);
    wuxm::PathGeometry geometry;
    geometry.Figures().Append(figure);
    wuxs::Path path;
    path.Data(geometry);
    path.Fill(fill);
    // Barra arriba (o a la izquierda, que se dibuja acostada y luego se transpone): el notch se voltea para quedar
    // pegado al borde de la pantalla
    if (g_settings.Edge == 1 || g_settings.Edge == 0)
    {
        wuxm::ScaleTransform flip;
        flip.ScaleY(-1);
        flip.CenterY((bottom - 1) / 2); // bottom = alto de la barra + 1: el espejo queda justo contra el borde de arriba
        path.RenderTransform(flip);
    }
    canvas.Children().Append(path);
}

// Una isla con el skin elegido
static void AddIsland(wuxc::Canvas const& canvas, double x, double y, double w, double h, int group, double barHeight)
{
    int skin = g_settings.Skin;
    if (skin == SkinGhost) return;                    // Solo los iconos
    if (skin == SkinDock && group != 1) return;       // Solo la píldora de las apps
    double radius = IslandRadius(h);
    if (skin == SkinNotch)
    {
        AddNotchShape(canvas, x, y + 2, w, barHeight + 1, 18, IslandFill(group));
        return;
    }
    if (skin == SkinGlass || skin == SkinAero7 || skin == SkinAurora || skin == SkinDock)
    {
        AddRect(canvas, x - 2, y - 1, w + 4, h + 4, radius + 2, Argb(30, 0, 0, 0), Color{}, 0);   // Sombra suave
        AddRect(canvas, x - 1, y, w + 2, h + 2, radius + 1, Argb(45, 0, 0, 0), Color{}, 0);
    }
    if (skin == SkinNeon)
    {
        Color glow = group == 3 ? Argb(255, 244, 114, 182) : Argb(255, 56, 189, 248);
        AddRect(canvas, x - 3, y - 3, w + 6, h + 6, radius + 3, Color{ 28, glow.R, glow.G, glow.B }, Color{}, 0);
        AddRect(canvas, x - 1.5, y - 1.5, w + 3, h + 3, radius + 1.5, Color{ 50, glow.R, glow.G, glow.B }, Color{}, 0);
    }
    wuxs::Rectangle rect;
    rect.Width(std::max(0.0, w));
    rect.Height(std::max(0.0, h));
    rect.RadiusX(radius);
    rect.RadiusY(radius);
    wuxm::Brush fill = IslandFill(group);
    if (g_settings.Frosted) fill.Opacity(0.62);
    rect.Fill(fill);
    double stroke = StrokeWidth();
    if (stroke > 0)
    {
        rect.Stroke(IslandStroke(group));
        rect.StrokeThickness(stroke);
    }
    wuxc::Canvas::SetLeft(rect, x);
    wuxc::Canvas::SetTop(rect, y);
    canvas.Children().Append(rect);
    if (skin == SkinLiquid) // Reflejo fino arriba
    {
        AddRect(canvas, x + radius * 0.6, y + 1.5, std::max(0.0, w - radius * 1.2), 1.2, 0.6, Argb(150, 255, 255, 255), Color{}, 0);
    }
}

// Corre la bandeja con RenderTransform (los clics la siguen). El clima no se puede mover: se dibuja en una capa de
// animación propia que ignora Translation y RenderTransform, y con margen Windows lo saca de la barra.
static void SetShift(wux::FrameworkElement const& element, float dx)
{
    void* key = winrt::get_abi(element);
    auto it = g_shift.find(key);
    float current = it == g_shift.end() ? 0.0f : it->second;
    if (std::abs(current - dx) < 0.5f) return;
    wuxm::TranslateTransform move = element.RenderTransform().try_as<wuxm::TranslateTransform>();
    if (!move)
    {
        if (dx == 0.0f) { g_shift.erase(key); return; }
        move = wuxm::TranslateTransform();
        element.RenderTransform(move);
    }
    move.X(dx);
    if (dx == 0.0f) g_shift.erase(key); else g_shift[key] = dx;
}

static void StyleBackground(InstanceHandle handle)
{
    wux::UIElement element = ElementFrom(handle);
    if (!element) return;
    if (!g_originalOpacity.count(handle)) g_originalOpacity[handle] = element.Opacity();
    element.Opacity(g_settings.Active ? 0.0 : g_originalOpacity[handle]);
}

static void PlacePlayer(wuxc::Grid const& root, double x, double top, double height);
static void ApplyStartIcon(FrameState& state);

// Skins claros (Papel, Hielo): las letras e iconos de Windows (hora, clima, bandeja) se pintan oscuros; al volver a
// un skin oscuro se les quita el color y Windows pone el suyo
static std::vector<winrt::weak_ref<wux::DependencyObject>> g_tinted;
static bool g_tintOn = false;

static void TintText(wux::DependencyObject const& node, wuxm::Brush const& ink, int depth = 0)
{
    if (depth > 60) return;
    int count = wuxm::VisualTreeHelper::GetChildrenCount(node);
    for (int i = 0; i < count; i++)
    {
        wux::DependencyObject child = wuxm::VisualTreeHelper::GetChild(node, i);
        if (auto text = child.try_as<wuxc::TextBlock>())
        {
            text.Foreground(ink);
            g_tinted.push_back(winrt::make_weak(child));
        }
        else if (auto icon = child.try_as<wuxc::IconElement>())
        {
            icon.Foreground(ink);
            g_tinted.push_back(winrt::make_weak(child));
        }
        TintText(child, ink, depth + 1);
    }
}

static void ApplyInk(wux::FrameworkElement const& anyPiece)
{
    bool light = g_settings.Active && LightSkin();
    if (!light && !g_tintOn) return;
    if (!light)
    {
        for (auto& weak : g_tinted)
        {
            wux::DependencyObject element = weak.get();
            if (!element) continue;
            if (auto text = element.try_as<wuxc::TextBlock>()) text.ClearValue(wuxc::TextBlock::ForegroundProperty());
            else if (auto icon = element.try_as<wuxc::IconElement>()) icon.ClearValue(wuxc::IconElement::ForegroundProperty());
        }
        g_tinted.clear();
        g_tintOn = false;
        return;
    }
    // Desde la raíz de la ventana de la barra: incluye la bandeja, el reloj y el clima
    wux::DependencyObject top = anyPiece;
    for (int i = 0; i < 80; i++)
    {
        wux::DependencyObject parent = wuxm::VisualTreeHelper::GetParent(top);
        if (!parent) break;
        top = parent;
    }
    g_tinted.clear();
    TintText(top, wuxm::SolidColorBrush(Argb(255, 22, 24, 30)));
    g_tintOn = true;
}

static void LayoutFrame(FrameState& state)
{
    wuxc::Grid root = state.Root.get();
    wux::FrameworkElement repeater = state.Repeater.get();
    if (!root || !repeater) return;
    // Barra vertical (a un lado): la primera vez se guarda su árbol para diseñar las islas verticales
    static bool verticalDumped = false;
    if (!verticalDumped && root.ActualHeight() > root.ActualWidth() && root.ActualWidth() > 1)
    {
        verticalDumped = true;
        Log(L"Barra vertical: " + std::to_wstring((int)root.ActualWidth()) + L"×" + std::to_wstring((int)root.ActualHeight()) + L", árbol en barra_arbol_vertical.txt");
        DumpTree(L"barra_arbol_vertical.txt");
    }
    try { ApplyStartIcon(state); } catch (...) { }
    try { ApplyInk(root); } catch (...) { }

    // Barra a un lado: todo se calcula «acostado» (y como x) y al final el dibujo se gira con una transposición
    const bool vertical = root.ActualHeight() > root.ActualWidth();
    auto lay = [vertical](Rect r) { return vertical ? Rect{ r.Y, r.X, r.Height, r.Width } : r; };

    // Piezas reales de la barra en tres grupos: 0 clima, 1 inicio + búsqueda + apps, 2 bandeja + reloj
    struct Item { wux::FrameworkElement Element; int Group; Rect Bounds; };
    std::vector<Item> items;
    double minX[3] = { 1e9, 1e9, 1e9 }, maxX[3] = { -1e9, -1e9, -1e9 };
    int count = wuxm::VisualTreeHelper::GetChildrenCount(repeater);
    for (int i = 0; i < count; i++)
    {
        wux::FrameworkElement element = wuxm::VisualTreeHelper::GetChild(repeater, i).try_as<wux::FrameworkElement>();
        if (!element) continue;
        std::wstring className(winrt::get_class_name(element));
        int group = className == L"Taskbar.AugmentedEntryPointButton" ? 0 : HasDescendantClass(element, L"SystemTray.SystemTrayFrame") ? 2 : 1;
        if (element.Visibility() != wux::Visibility::Visible || element.ActualWidth() < 1)
        {
            SetShift(element, 0);
            continue;
        }
        // El botón del clima es más ancho que lo que muestra: se mide solo lo visible (ícono y textos)
        Rect bounds = element.TransformToVisual(root).TransformBounds(Rect{ 0, 0, (float)element.ActualWidth(), (float)element.ActualHeight() });
        // ItemsRepeater guarda los botones que recicla fuera de la pantalla (x = -10000): no cuentan
        if (bounds.X + bounds.Width < -200 || bounds.X > 20000 || bounds.Y + bounds.Height < -200 || bounds.Y > 20000) continue;
        if (group == 0 && !vertical)
        {
            double left = 1e9, right = -1e9;
            VisibleExtent(element, root, left, right, element.Translation().x);
            if (left < right) { bounds.X = (float)left; bounds.Width = (float)(right - left); }
        }
        auto shift = g_shift.find(winrt::get_abi(element));
        if (shift != g_shift.end() && !vertical) bounds.X -= shift->second;  // Posición natural, sin nuestro desplazamiento
        bounds = lay(bounds);
        items.push_back({ element, group, bounds });
        minX[group] = std::min(minX[group], (double)bounds.X);
        maxX[group] = std::max(maxX[group], (double)(bounds.X + bounds.Width));
    }

    // Cambió el número de iconos visibles (una app abrió o cerró): mediciones rápidas mientras se animan. No por
    // cualquier movimiento de la barra (indicadores de audio, etc.), que pasa todo el tiempo
    if ((int)items.size() != state.Pieces)
    {
        state.Pieces = (int)items.size();
        state.Burst = 6;
    }

    // Otras versiones de Windows 11 no siempre tienen la bandeja o el clima dentro del repetidor: se buscan en toda
    // la barra. Solo se miden (no se desplazan). Si aun así falta la bandeja, se guarda el árbol para diagnosticar.
    wux::FrameworkElement frameElement = state.Frame.get();
    auto measureOutside = [&](wux::FrameworkElement const& element, int group)
    {
        if (!element || element.Visibility() != wux::Visibility::Visible || element.ActualWidth() < 1) return;
        try
        {
            Rect bounds = lay(element.TransformToVisual(root).TransformBounds(Rect{ 0, 0, (float)element.ActualWidth(), (float)element.ActualHeight() }));
            if (bounds.X + bounds.Width < -200 || bounds.X > 20000) return;
            minX[group] = std::min(minX[group], (double)bounds.X);
            maxX[group] = std::max(maxX[group], (double)(bounds.X + bounds.Width));
        }
        catch (...) { } // Está en otra ventana de la barra: no se puede medir desde aquí
    };
    // En algunas versiones (26300) la bandeja no está dentro del marco sino a su lado, en la misma raíz XAML: se busca
    // también ahí. A esa bandeja no la mueve el marco, así que se dibuja donde está, sin desplazarla.
    bool traySibling = false;
    if (frameElement && minX[2] >= maxX[2])
    {
        wux::FrameworkElement tray = FindByClass(frameElement, L"SystemTray.SystemTrayFrame");
        if (!tray)
        {
            tray = FindByClass(wuxm::VisualTreeHelper::GetParent(frameElement), L"SystemTray.SystemTrayFrame");
            traySibling = (bool)tray;
        }
        measureOutside(tray, 2);
        if (traySibling && !state.TraySiblingLogged)
        {
            state.TraySiblingLogged = true;
            Log(L"La bandeja está al lado del marco de la barra (no dentro): se mide ahí");
        }
    }
    if (frameElement && minX[0] >= maxX[0])
    {
        wux::FrameworkElement widgets = FindByClass(frameElement, L"Taskbar.AugmentedEntryPointButton");
        if (!widgets) widgets = FindByName(frameElement, L"WidgetsButton");
        measureOutside(widgets, 0);
    }
    if (minX[2] >= maxX[2] && g_settings.Active && !state.Diagnosed)
    {
        state.Diagnosed = true;
        Log(L"No se encontró la bandeja dentro de la barra: se guarda el árbol en barra_arbol.txt");
        DumpTree();
    }

    const double width = vertical ? root.ActualHeight() : root.ActualWidth(), height = vertical ? root.ActualWidth() : root.ActualHeight();
    // Vertical: la isla ocupa todo el grueso de la barra (los iconos y el clima no caben en 40 px de 48)
    const double pad = vertical ? 10 : 8, islandHeight = vertical ? height : std::min(height - 2, 46.0), top = (height - islandHeight) / 2;
    bool present[3];
    double islandWidth[3];
    for (int g = 0; g < 3; g++)
    {
        present[g] = minX[g] < maxX[g];
        islandWidth[g] = present[g] ? maxX[g] - minX[g] + 2 * pad : 0;
    }

    // Formatos centrados (juntas o cápsula): se estrecha el marco de la barra y Windows mismo junta las piezas
    // (clima a la izquierda del marco, apps al centro, bandeja a la derecha). Igual que los temas dock del Styler.
    double shiftOf[3] = { 0, 0, 0 };
    bool centered = g_settings.Active && !vertical && g_settings.Format != Full && !(g_settings.Format == Islands && g_settings.Edges);
    double gap = g_settings.Format == Capsule ? 0 : g_settings.Gap;
    double margin = 0;
    wux::FrameworkElement frame = state.Frame.get();
    double fullWidth = frame && frame.XamlRoot() ? frame.XamlRoot().Size().Width : width;
    // El marco mide lo justo para el clima a la izquierda y su reflejo a la derecha, con las apps al centro. La
    // bandeja (más ancha o más angosta que el clima) se corre hasta quedar a la distancia elegida de las apps, y el
    // marco se desplaza la mitad hacia el otro lado para que el grupo entero quede centrado en la pantalla.
    double left = 0, right = 0;
    bool atRest = frame && frame.Margin().Left == 0 && frame.Margin().Right == 0;
    if (atRest)
    {
        // Barra a tamaño normal: se toman las medidas de verdad
        state.Calibrated = present[1];
        state.CalibratedCount = items.size();
        state.Side = present[0] ? maxX[0] + pad : 0;
        state.Apps = islandWidth[1];
        // Bandeja al lado del marco: no se puede acercar a las apps, se queda en su sitio (ancho 0 = no se corre)
        state.TrayWidth = traySibling ? 0 : islandWidth[2];
        state.TrayInset = present[2] && !traySibling ? width - (maxX[2] + pad) : 0;
    }
    else if (state.Calibrated && (islandWidth[1] < state.Apps - 12 || (state.Side > 0 && present[0] && maxX[0] + pad < state.Side - 12)))
    {
        // Windows pasó a modo compacto (clima solo ícono, búsqueda como lupa) porque el marco quedó justo: más holgura
        state.Slack = std::min(state.Slack + 40, 400.0);
        Log(L"Windows compactó la barra: holgura " + std::to_wstring((int)state.Slack));
        frame.Margin(wux::Thickness{ 0, 0, 0, 0 });
        for (Item& item : items) SetShift(item.Element, 0);
        state.GroupShift[2] = 0;
        return;
    }
    else if (items.size() != state.CalibratedCount)
    {
        // Se abrió o cerró una app: se vuelve a medir con la barra a tamaño normal. Si faltan piezas puede ser que
        // Windows las escondiera por falta de espacio: la próxima vez el marco lleva más holgura.
        if (items.size() < state.CalibratedCount) state.Slack = std::min(state.Slack + 24, 200.0);
        Log(L"Se vuelve a medir la barra (" + std::to_wstring(items.size()) + L" piezas, holgura " + std::to_wstring((int)state.Slack) + L")");
        frame.Margin(wux::Thickness{ 0, 0, 0, 0 });
        for (Item& item : items) SetShift(item.Element, 0);
        state.GroupShift[2] = 0;
        return;
    }
    if (centered && state.Calibrated)
    {
        // Todo sale de las medidas guardadas, nunca de la barra ya estrechada (si no, se encoge sin fin)
        const double slack = state.Slack;
        double side = state.Side > 0 ? state.Side + gap : 0;
        double frameWidth = state.Apps + 2 * side + slack;
        double base = std::max(0.0, std::floor((fullWidth - frameWidth) / 2));
        if (state.TrayWidth > 0)
        {
            double appsRight = frameWidth / 2 + state.Apps / 2;
            double trayLeft = frameWidth - state.TrayInset - state.TrayWidth;
            shiftOf[2] = std::round(appsRight + gap - trayLeft);
        }
        state.GroupShift[2] = shiftOf[2];
        left = std::max(0.0, base - std::floor(shiftOf[2] / 2));
        right = std::max(0.0, base + std::floor(shiftOf[2] / 2));
    }
    else
    {
        state.GroupShift[2] = 0;  // A los extremos, completa o apagado: todo en su sitio
    }
    if (frame && (std::abs(frame.Margin().Left - left) > 2 || std::abs(frame.Margin().Right - right) > 2))
    {
        frame.Margin(wux::Thickness{ left, 0, right, 0 });
        for (Item& item : items) SetShift(item.Element, (float)shiftOf[item.Group]);
        return;  // Windows reacomoda la barra y LayoutUpdated nos vuelve a llamar
    }
    shiftOf[2] = state.GroupShift[2];
    for (int g = 0; g < 3; g++) minX[g] = std::round(minX[g] / 3) * 3, maxX[g] = std::round(maxX[g] / 3) * 3;
    for (int g = 0; g < 3; g++) islandWidth[g] = present[g] ? maxX[g] - minX[g] + 2 * pad : 0;
    for (Item& item : items) SetShift(item.Element, (float)shiftOf[item.Group]);

    // Mini reproductor: a la derecha de la isla de las apps (solo en la barra principal)
    if (!g_frames.empty() && g_frames[0].get() == &state && present[1] && !vertical)
    {
        PlacePlayer(root, maxX[1] + shiftOf[1] + pad + 14, top, islandHeight);
    }

    // Firma de lo que hay que dibujar
    wchar_t signature[256];
    swprintf_s(signature, L"%d|%d|%d|%d|%d.%d%d%d|%x|%.0f|%.0f|%.0f,%.0f|%.0f,%.0f|%.0f,%.0f", g_settings.Active, g_settings.Format, g_settings.Gap,
        g_settings.Edges, g_settings.Skin, g_settings.Line, (int)g_settings.Frosted, (int)g_settings.Rounded, *(uint32_t*)&g_settings.IslandColor, width, height, minX[0] + shiftOf[0], islandWidth[0], minX[1] + shiftOf[1], islandWidth[1],
        minX[2] + shiftOf[2], islandWidth[2]);
    if (state.Drawn == signature) return;
    state.Drawn = signature;
    Log(L"Islas: " + std::to_wstring(items.size()) + L" piezas · " + signature);

    wuxc::Canvas canvas = state.Canvas.get();
    if (!g_settings.Active)
    {
        if (canvas)
        {
            uint32_t index;
            if (root.Children().IndexOf(canvas, index)) root.Children().RemoveAt(index);
            state.Canvas = nullptr;
        }
        return;
    }
    if (!canvas)
    {
        canvas = wuxc::Canvas();
        canvas.IsHitTestVisible(false);
        wuxc::Grid::SetRowSpan(canvas, 16);
        wuxc::Grid::SetColumnSpan(canvas, 16);
        root.Children().InsertAt(std::min<uint32_t>(1, root.Children().Size()), canvas);  // Detrás de los iconos
        state.Canvas = canvas;
    }
    canvas.Children().Clear();
    for (auto& story : g_animations) story.Stop();  // Los bordes RGB anteriores ya no se ven
    g_animations.clear();
    // Vertical: lo dibujado «acostado» se transpone (x ↔ y) para quedar a lo largo de la barra
    if (vertical)
    {
        wuxm::MatrixTransform transpose;
        transpose.Matrix(wuxm::Matrix{ 0, 1, 1, 0, 0, 0 });
        canvas.RenderTransform(transpose);
    }
    else
    {
        canvas.RenderTransform(nullptr);
    }

    // Completa o Línea fina: una barra de lado a lado (Línea fina: delgada y con divisiones entre los grupos)
    if (g_settings.Format == Full || g_settings.Skin == SkinLine)
    {
        wuxs::Rectangle bar;
        bar.Width(width);
        bar.Height(height);
        bar.Fill(g_settings.Skin == SkinGhost ? wuxm::SolidColorBrush(Color{ 0, 0, 0, 0 }) : IslandFill(1));
        canvas.Children().Append(bar);
        AddRect(canvas, 0, 0, width, 1, 0, Argb(30, 255, 255, 255), Color{}, 0);
        if (g_settings.Skin == SkinLine)
        {
            for (int g = 0; g < 3; g++)
            {
                if (!present[g] || g == 2) continue;
                AddRect(canvas, maxX[g] + shiftOf[g] + pad, 10, 1, height - 20, 0, Argb(30, 255, 255, 255), Color{}, 0);
                if (present[g + 1]) AddRect(canvas, minX[g + 1] + shiftOf[g + 1] - pad, 10, 1, height - 20, 0, Argb(30, 255, 255, 255), Color{}, 0);
            }
        }
        return;
    }
    if (g_settings.Format == Capsule)
    {
        double left = 1e9, right = -1e9;
        for (int g = 0; g < 3; g++)
        {
            if (!present[g]) continue;
            left = std::min(left, minX[g] + shiftOf[g] - pad);
            right = std::max(right, maxX[g] + shiftOf[g] + pad);
        }
        if (left >= right) return;
        AddIsland(canvas, left, top, right - left, islandHeight, 1, height);
        for (int g = 0; g < 2; g++)
        {
            if (!present[g] || !present[g + 1]) continue;
            double x = maxX[g] + shiftOf[g] + pad;  // Separador fino entre grupos
            AddRect(canvas, x, top + 10, 1, islandHeight - 20, 0, Argb(46, 255, 255, 255), Color{}, 0);
        }
        return;
    }
    for (int g = 0; g < 3; g++)
    {
        if (present[g]) AddIsland(canvas, minX[g] + shiftOf[g] - pad, top, islandWidth[g], islandHeight, g, height);
    }
}

#pragma region Mini reproductor (4.ª isla)

namespace wmc = winrt::Windows::Media::Control;

// La música la lee un hilo aparte (Windows avisa de cambios); la isla se actualiza en el hilo de la barra
static std::mutex g_mediaLock;
static wmc::GlobalSystemMediaTransportControlsSessionManager g_mediaManager{ nullptr };
static wmc::GlobalSystemMediaTransportControlsSession g_session{ nullptr };
static winrt::weak_ref<wuxc::Canvas> g_playerCanvas;
static winrt::weak_ref<wuxc::Border> g_playerIsland, g_playerArt;
static winrt::weak_ref<wuxc::TextBlock> g_playerTitle, g_playerArtist;
static winrt::weak_ref<wuxc::FontIcon> g_playerPlay;
static bool g_hasMedia = false;
static double g_playerX = -1;
// Lo último que se supo de la canción: se aplica también cuando la isla se crea después
static std::wstring g_lastTitle, g_lastArtist;
static bool g_lastPlaying = false;
static winrt::Windows::Storage::Streams::IRandomAccessStreamWithContentType g_lastThumbnail{ nullptr };

static void ApplyMediaToPlayer()
{
    if (auto t = g_playerTitle.get()) t.Text(g_lastTitle);
    if (auto a = g_playerArtist.get()) a.Text(g_lastArtist);
    if (auto p = g_playerPlay.get()) p.Glyph(g_lastPlaying ? L"" : L"");
    if (auto art = g_playerArt.get())
    {
        if (g_lastThumbnail)
        {
            wuxm::Imaging::BitmapImage image;
            image.DecodePixelWidth(56);
            g_lastThumbnail.Seek(0);
            image.SetSourceAsync(g_lastThumbnail);
            wuxm::ImageBrush brush;
            brush.ImageSource(image);
            brush.Stretch(wuxm::Stretch::UniformToFill);
            art.Background(brush);
        }
    }
}

static wmc::GlobalSystemMediaTransportControlsSession CurrentSession()
{
    std::lock_guard<std::mutex> lock(g_mediaLock);
    return g_session;
}

static wuxc::Button PlayerButton(const wchar_t* glyph, wuxc::FontIcon* iconOut)
{
    wuxc::Button button;
    button.Background(wuxm::SolidColorBrush(Color{ 0, 0, 0, 0 }));
    button.BorderThickness(wux::Thickness{ 0, 0, 0, 0 });
    button.Padding(wux::Thickness{ 6, 4, 6, 4 });
    button.MinWidth(0);
    button.MinHeight(0);
    wuxc::FontIcon icon;
    icon.Glyph(glyph);
    icon.FontSize(13);
    icon.Foreground(wuxm::SolidColorBrush(Color{ 255, 255, 255, 255 }));
    button.Content(icon);
    if (iconOut) *iconOut = icon;
    return button;
}

// Crea la isla del reproductor (una vez) encima de la barra
static wuxc::Canvas EnsurePlayer(wuxc::Grid const& root)
{
    wuxc::Canvas canvas = g_playerCanvas.get();
    if (canvas) return canvas;
    canvas = wuxc::Canvas();  // Sin fondo: solo la isla recibe clics
    wuxc::Grid::SetRowSpan(canvas, 16);
    wuxc::Grid::SetColumnSpan(canvas, 16);

    wuxc::Border island;
    island.CornerRadius(wux::CornerRadius{ 20, 20, 20, 20 });
    island.Padding(wux::Thickness{ 6, 0, 4, 0 });
    wuxc::StackPanel row;
    row.Orientation(wuxc::Orientation::Horizontal);
    row.VerticalAlignment(wux::VerticalAlignment::Center);

    wuxc::Border art;
    art.Width(28);
    art.Height(28);
    art.CornerRadius(wux::CornerRadius{ 7, 7, 7, 7 });
    art.Background(wuxm::SolidColorBrush(Color{ 255, 124, 58, 237 }));
    art.VerticalAlignment(wux::VerticalAlignment::Center);
    row.Children().Append(art);

    wuxc::StackPanel text;
    text.Margin(wux::Thickness{ 8, 0, 6, 0 });
    text.Width(120);
    text.VerticalAlignment(wux::VerticalAlignment::Center);
    wuxc::TextBlock title;
    title.FontSize(12);
    title.FontWeight(winrt::Windows::UI::Text::FontWeight{ 600 });
    title.Foreground(wuxm::SolidColorBrush(Color{ 255, 255, 255, 255 }));
    title.TextTrimming(wux::TextTrimming::CharacterEllipsis);
    wuxc::TextBlock artist;
    artist.FontSize(11);
    artist.Foreground(wuxm::SolidColorBrush(Color{ 255, 154, 164, 181 }));
    artist.TextTrimming(wux::TextTrimming::CharacterEllipsis);
    text.Children().Append(title);
    text.Children().Append(artist);
    row.Children().Append(text);

    wuxc::FontIcon playIcon{ nullptr };
    wuxc::Button previous = PlayerButton(L"", nullptr);
    wuxc::Button play = PlayerButton(L"", &playIcon);
    wuxc::Button next = PlayerButton(L"", nullptr);
    previous.Click([](auto&&, auto&&) { auto s = CurrentSession(); if (s) s.TrySkipPreviousAsync(); });
    play.Click([](auto&&, auto&&) { auto s = CurrentSession(); if (s) s.TryTogglePlayPauseAsync(); });
    next.Click([](auto&&, auto&&) { auto s = CurrentSession(); if (s) s.TrySkipNextAsync(); });
    row.Children().Append(previous);
    row.Children().Append(play);
    row.Children().Append(next);
    island.Child(row);
    canvas.Children().Append(island);
    root.Children().Append(canvas);  // Encima de todo

    g_playerCanvas = canvas;
    g_playerIsland = island;
    g_playerArt = art;
    g_playerTitle = title;
    g_playerArtist = artist;
    g_playerPlay = playIcon;
    g_playerX = -1;
    ApplyMediaToPlayer();
    return canvas;
}

static void RemovePlayer(wuxc::Grid const& root)
{
    wuxc::Canvas canvas = g_playerCanvas.get();
    if (!canvas) return;
    uint32_t index;
    if (root.Children().IndexOf(canvas, index)) root.Children().RemoveAt(index);
    g_playerCanvas = nullptr;
}

// Coloca la isla del reproductor a la derecha de la de las apps (o la quita si no toca)
static std::wstring g_playerStyle;  // Skin y color con los que se pintó el reproductor (para no repintarlo en cada pasada)

static void PlacePlayer(wuxc::Grid const& root, double x, double top, double height)
{
    bool wanted = g_settings.Active && g_settings.MiniPlayer && g_hasMedia && g_settings.Format != Capsule;
    if (!wanted) { RemovePlayer(root); g_playerStyle.clear(); return; }
    bool created = !g_playerCanvas.get();
    EnsurePlayer(root);
    wuxc::Border island = g_playerIsland.get();
    if (!island) return;

    // El reproductor lleva el mismo skin que las islas (en Completa y Línea fina va sin fondo, dentro de la barra)
    wchar_t style[64];
    swprintf_s(style, L"%d|%d|%x|%.0f", g_settings.Skin, g_settings.Format, *(uint32_t*)&g_settings.IslandColor, height);
    if (created || g_playerStyle != style)
    {
        g_playerStyle = style;
        bool inline_ = g_settings.Format == Full || g_settings.Skin == SkinLine || g_settings.Skin == SkinGhost;
        bool notch = g_settings.Skin == SkinNotch && !inline_;
        double barHeight = root.ActualHeight();
        island.Height(notch ? barHeight - top - 1 : height);
        island.CornerRadius(notch ? wux::CornerRadius{ 18, 18, 0, 0 }
            : g_settings.Skin == SkinAero7 ? wux::CornerRadius{ 10, 10, 10, 10 } : wux::CornerRadius{ height / 2, height / 2, height / 2, height / 2 });
        island.Background(inline_ ? wuxm::SolidColorBrush(Color{ 0, 0, 0, 0 }) : IslandFill(3));
        double stroke = inline_ || notch ? 0 : StrokeWidth();
        island.BorderThickness(wux::Thickness{ stroke, stroke, stroke, stroke });
        if (stroke > 0) island.BorderBrush(IslandStroke(3));
        g_playerX = -1;
    }
    if (std::abs(g_playerX - x) > 1)
    {
        bool notch = g_settings.Skin == SkinNotch && g_settings.Format != Full;
        wuxc::Canvas::SetLeft(island, x);
        wuxc::Canvas::SetTop(island, notch ? top + 2 : top);
        g_playerX = x;
    }
}

// Datos de la canción: se piden fuera del hilo de la barra y se aplican en él
static void RefreshMedia()
{
    try
    {
        wmc::GlobalSystemMediaTransportControlsSession session = CurrentSession();
        std::wstring title, artist;
        bool playing = false;
        winrt::Windows::Storage::Streams::IRandomAccessStreamWithContentType thumbnail{ nullptr };
        if (session)
        {
            auto properties = session.TryGetMediaPropertiesAsync().get();
            title = properties.Title();
            artist = properties.Artist();
            playing = session.GetPlaybackInfo().PlaybackStatus() == wmc::GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing;
            if (properties.Thumbnail())
            {
                try { thumbnail = properties.Thumbnail().OpenReadAsync().get(); } catch (...) { }
            }
        }
        wux::UIElement anchor = g_anchor.get();
        if (!anchor) return;
        anchor.Dispatcher().RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Normal, [session, title, artist, playing, thumbnail]
        {
            try
            {
                g_hasMedia = session != nullptr && !title.empty();
                g_lastTitle = title;
                g_lastArtist = artist;
                g_lastPlaying = playing;
                g_lastThumbnail = thumbnail;
                ApplyMediaToPlayer();
                for (auto& frame : g_frames) frame->Drawn.clear(); // Que la próxima pasada coloque o quite la isla
                if (!g_frames.empty())
                {
                    auto first = g_frames[0];
                    if (auto root = first->Root.get()) root.InvalidateArrange();
                }
            }
            catch (...) { }
        });
    }
    catch (...) { }
}

static void WatchSession(wmc::GlobalSystemMediaTransportControlsSession const& session)
{
    if (!session) return;
    session.MediaPropertiesChanged([](auto&&, auto&&) { RefreshMedia(); });
    session.PlaybackInfoChanged([](auto&&, auto&&) { RefreshMedia(); });
}

static void StartMedia()
{
    std::thread([]
    {
        try
        {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            auto manager = wmc::GlobalSystemMediaTransportControlsSessionManager::RequestAsync().get();
            {
                std::lock_guard<std::mutex> lock(g_mediaLock);
                g_mediaManager = manager;
                g_session = manager.GetCurrentSession();
            }
            WatchSession(CurrentSession());
            manager.CurrentSessionChanged([](auto&& sender, auto&&)
            {
                auto current = sender.GetCurrentSession();
                { std::lock_guard<std::mutex> lock(g_mediaLock); g_session = current; }
                WatchSession(current);
                RefreshMedia();
            });
            RefreshMedia();
            Log(L"Mini reproductor conectado a la música de Windows");
        }
        catch (winrt::hresult_error const& error)
        {
            Log(L"Mini reproductor sin música: " + std::wstring(error.message()));
        }
    }).detach();
}

#pragma endregion

#pragma region Icono de Inicio

// Icono del botón de Inicio (mockup «Icono de Inicio» aprobado). Se esconde el icono animado de Windows y en su
// misma celda se pone el nuestro: la imagen que prepara la suite o, con «Ojitos», dos ojos que parpadean.
// El botón sigue siendo el de Windows (clic, menú, arrastrar).

#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "shcore.lib")

static wux::FrameworkElement FindStartButton(wux::FrameworkElement const& repeater)
{
    int count = wuxm::VisualTreeHelper::GetChildrenCount(repeater);
    for (int i = 0; i < count; i++)
    {
        wux::FrameworkElement element = wuxm::VisualTreeHelper::GetChild(repeater, i).try_as<wux::FrameworkElement>();
        if (!element) continue;
        std::wstring name(element.Name());
        if (name == L"StartButton" || name == L"LaunchListButton") return element;
    }
    return nullptr;
}

// La imagen se lee del archivo a memoria (sin ms-appx ni rutas file:, que la barra no siempre acepta)
static wuxm::ImageSource LoadStartImage(std::wstring const& path)
{
    if (path.empty()) return nullptr;
    winrt::com_ptr<IStream> stream;
    if (FAILED(SHCreateStreamOnFileEx(path.c_str(), STGM_READ | STGM_SHARE_DENY_NONE, 0, FALSE, nullptr, stream.put())))
    {
        Log(L"Icono de Inicio: no se pudo abrir " + path);
        return nullptr;
    }
    winrt::Windows::Storage::Streams::IRandomAccessStream random{ nullptr };
    if (FAILED(CreateRandomAccessStreamOverStream(stream.get(), BSOS_DEFAULT, winrt::guid_of<winrt::Windows::Storage::Streams::IRandomAccessStream>(), winrt::put_abi(random))))
    {
        return nullptr;
    }
    wuxm::Imaging::BitmapImage bitmap;
    bitmap.SetSource(random);
    return bitmap;
}

// Ojitos que imitan a la isla: una imagen por cara (solo se ve la actual) y una capa que se mueve hacia el ratón
static std::vector<std::pair<wchar_t, winrt::weak_ref<wuxc::Image>>> g_faceImages;
static winrt::weak_ref<wux::FrameworkElement> g_eyesLayer;
static wux::DispatcherTimer g_followTimer{ nullptr };
static wchar_t g_startFace = L'n';

static void ShowStartFace(wchar_t face)
{
    g_startFace = face;
    for (auto& entry : g_faceImages)
    {
        wuxc::Image image = entry.second.get();
        if (!image) continue;
        bool show = face == L'n' ? (entry.first == L'n' || entry.first == L'c') : entry.first == face;
        image.Visibility(show ? wux::Visibility::Visible : wux::Visibility::Collapsed);
    }
}

static wchar_t ReadStartFace()
{
    std::wifstream file(DataFolder() + L"\\inicio_cara.txt");
    wchar_t face = L'n';
    if (file) file >> face;
    return std::wstring(L"ncfsdzaew").find(face) == std::wstring::npos ? L'n' : face;
}

// La suite avisa cuando la carita de la isla cambia de cara
static void ListenForStartFace()
{
    HANDLE changed = CreateEventW(nullptr, FALSE, FALSE, L"Local\\StreeckSuite.StartFace");
    for (;;)
    {
        WaitForSingleObject(changed, INFINITE);
        wux::UIElement anchor = g_anchor.get();
        if (!anchor) continue;
        wchar_t face = ReadStartFace();
        anchor.Dispatcher().RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Normal, [face] { try { ShowStartFace(face); } catch (...) { } });
    }
}

// Los ojos se corren un poquito hacia el ratón (12 veces por segundo; casi sin gasto)
static void FollowCursor()
{
    // Si el ratón no se movió no hay nada que calcular (casi todo el tiempo)
    static POINT last = { -1, -1 };
    POINT cursor;
    if (!GetCursorPos(&cursor) || (cursor.x == last.x && cursor.y == last.y)) return;
    last = cursor;
    wux::FrameworkElement layer = g_eyesLayer.get();
    if (!layer || layer.ActualWidth() < 1) return;
    RECT bar;
    HWND tray = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!tray || !GetWindowRect(tray, &bar)) return;
    auto origin = layer.TransformToVisual(nullptr).TransformPoint(winrt::Windows::Foundation::Point{ 0, 0 });
    double cx = bar.left + origin.X + layer.ActualWidth() / 2, cy = bar.top + origin.Y + layer.ActualHeight() / 2;
    double dx = cursor.x - cx, dy = cursor.y - cy, d = std::max(1.0, std::sqrt(dx * dx + dy * dy));
    double reach = std::min(1.0, d / 300) * layer.ActualWidth() * 0.09;
    wuxm::TranslateTransform move = layer.RenderTransform().try_as<wuxm::TranslateTransform>();
    if (!move) return;
    double x = dx / d * reach, y = dy / d * reach * 0.7;
    if (std::abs(move.X() - x) > 0.05 || std::abs(move.Y() - y) > 0.05) { move.X(x); move.Y(y); }
}

static wux::FrameworkElement MakeStartIcon(double size)
{
    wuxc::Grid holder;
    holder.Name(L"StreeckStartIcon");
    holder.Width(size);
    holder.Height(size);
    holder.HorizontalAlignment(wux::HorizontalAlignment::Center);
    holder.VerticalAlignment(wux::VerticalAlignment::Center);
    holder.IsHitTestVisible(false);
    wuxm::CompositeTransform transform;
    transform.CenterX(size / 2);
    transform.CenterY(size / 2);
    holder.RenderTransform(transform);

    if (g_settings.StartIcon == 3 && !g_settings.StartFacesBase.empty())
    {
        // Ojitos de la isla: píldora negra y encima las caras (normal parpadeando, feliz, sorpresa, dormido...)
        wuxc::Border pill;
        pill.Background(wuxm::SolidColorBrush(Color{ 0, 0, 0, 0 })); // Sin fondo propio: los ojitos van directo sobre la isla (con cualquier skin)
        pill.Height(size * 84 / 128);
        pill.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(size * 42 / 128));
        pill.VerticalAlignment(wux::VerticalAlignment::Center);
        holder.Children().Append(pill);
        wuxc::Grid layer;
        layer.RenderTransform(wuxm::TranslateTransform());
        holder.Children().Append(layer);
        // Varios monitores: cada barra tiene su botón; solo se quitan las caras que ya no existen
        g_faceImages.erase(std::remove_if(g_faceImages.begin(), g_faceImages.end(), [](auto& e) { return !e.second.get(); }), g_faceImages.end());
        for (wchar_t face : std::wstring(L"cnfsdzaew"))
        {
            wuxc::Image image;
            image.Source(LoadStartImage(g_settings.StartFacesBase + L"_" + face + L".png"));
            image.Stretch(wuxm::Stretch::Uniform);
            layer.Children().Append(image);
            g_faceImages.push_back({ face, image });
            if (face == L'n')
            {
                // Parpadeo: la cara abierta se apaga un instante cada 3,5 s y se ve la de los ojos cerrados
                wuxm::Animation::DoubleAnimationUsingKeyFrames blink;
                std::pair<int, double> keys[] = { { 0, 1.0 }, { 3360, 1.0 }, { 3361, 0.0 }, { 3500, 0.0 }, { 3501, 1.0 } };
                for (auto& key : keys)
                {
                    wuxm::Animation::DiscreteDoubleKeyFrame frame;
                    frame.KeyTime(wuxm::Animation::KeyTimeHelper::FromTimeSpan(std::chrono::milliseconds(key.first)));
                    frame.Value(key.second);
                    blink.KeyFrames().Append(frame);
                }
                blink.RepeatBehavior(wuxm::Animation::RepeatBehaviorHelper::Forever());
                wuxm::Animation::Storyboard story;
                wuxm::Animation::Storyboard::SetTarget(blink, image);
                wuxm::Animation::Storyboard::SetTargetProperty(blink, L"Opacity");
                story.Children().Append(blink);
                story.Begin();
            }
        }
        g_eyesLayer = layer;
        ShowStartFace(ReadStartFace());
        if (!g_followTimer)
        {
            g_followTimer = wux::DispatcherTimer();
            g_followTimer.Interval(std::chrono::milliseconds(80));
            g_followTimer.Tick([](auto&&, auto&&) { try { FollowCursor(); } catch (...) { } });
            g_followTimer.Start();
            std::thread(ListenForStartFace).detach();
        }
    }
    else if (g_settings.StartIcon == 3 && g_settings.StartFile.empty())
    {
        // Ojitos sin imagen de la suite (versión anterior): píldora negra con dos ojos blancos que parpadean
        wuxc::Border pill;
        pill.Background(wuxm::SolidColorBrush(Color{ 0, 0, 0, 0 })); // Sin fondo propio: los ojitos van directo sobre la isla (con cualquier skin)
        pill.Height(size * 0.75);
        pill.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(size * 0.375));
        pill.VerticalAlignment(wux::VerticalAlignment::Center);
        holder.Children().Append(pill);
        for (int i = 0; i < 2; i++)
        {
            wuxs::Rectangle eye;
            eye.Width(size * 0.19);
            eye.Height(size * 0.375);
            eye.RadiusX(size * 0.095);
            eye.RadiusY(size * 0.095);
            eye.Fill(wuxm::SolidColorBrush(Argb(255, 255, 255, 255)));
            eye.HorizontalAlignment(wux::HorizontalAlignment::Left);
            eye.VerticalAlignment(wux::VerticalAlignment::Center);
            eye.Margin(wux::ThicknessHelper::FromLengths(i == 0 ? size * 0.25 : size * 0.56, 0, 0, 0));
            wuxm::ScaleTransform scale;
            scale.CenterY(size * 0.1875);
            eye.RenderTransform(scale);
            holder.Children().Append(eye);

            wuxm::Animation::DoubleAnimationUsingKeyFrames blink;
            std::pair<int, double> keys[] = { { 0, 1.0 }, { 3360, 1.0 }, { 3430, 0.15 }, { 3500, 1.0 } };
            for (auto& key : keys)
            {
                wuxm::Animation::LinearDoubleKeyFrame frame;
                frame.KeyTime(wuxm::Animation::KeyTimeHelper::FromTimeSpan(std::chrono::milliseconds(key.first)));
                frame.Value(key.second);
                blink.KeyFrames().Append(frame);
            }
            blink.RepeatBehavior(wuxm::Animation::RepeatBehaviorHelper::Forever());
            wuxm::Animation::Storyboard story;
            wuxm::Animation::Storyboard::SetTarget(blink, scale);
            wuxm::Animation::Storyboard::SetTargetProperty(blink, L"ScaleY");
            story.Children().Append(blink);
            story.Begin();
        }
    }
    else
    {
        // Ojitos de la isla: debajo la cara con los ojos cerrados y encima la abierta, que se apaga un instante cada
        // 3,5 s para parpadear (lo anima el compositor)
        if (g_settings.StartIcon == 3 && !g_settings.StartClosedFile.empty())
        {
            wuxc::Image closed;
            closed.Source(LoadStartImage(g_settings.StartClosedFile));
            closed.Stretch(wuxm::Stretch::Uniform);
            holder.Children().Append(closed);
        }
        wuxc::Image image;
        image.Source(LoadStartImage(g_settings.StartFile));
        image.Stretch(wuxm::Stretch::Uniform);
        holder.Children().Append(image);
        if (g_settings.StartIcon == 3 && !g_settings.StartClosedFile.empty())
        {
            wuxm::Animation::DoubleAnimationUsingKeyFrames blink;
            std::pair<int, double> keys[] = { { 0, 1.0 }, { 3360, 1.0 }, { 3361, 0.0 }, { 3500, 0.0 }, { 3501, 1.0 } };
            for (auto& key : keys)
            {
                wuxm::Animation::DiscreteDoubleKeyFrame frame;
                frame.KeyTime(wuxm::Animation::KeyTimeHelper::FromTimeSpan(std::chrono::milliseconds(key.first)));
                frame.Value(key.second);
                blink.KeyFrames().Append(frame);
            }
            blink.RepeatBehavior(wuxm::Animation::RepeatBehaviorHelper::Forever());
            wuxm::Animation::Storyboard story;
            wuxm::Animation::Storyboard::SetTarget(blink, image);
            wuxm::Animation::Storyboard::SetTargetProperty(blink, L"Opacity");
            story.Children().Append(blink);
            story.Begin();
        }
    }
    return holder;
}

// Al pasar el ratón: crece o gira un poco (lo anima el compositor en 150 ms)
static void HoverStartIcon(winrt::Windows::Foundation::IInspectable const& sender, bool inside)
{
    wux::FrameworkElement button = sender.try_as<wux::FrameworkElement>();
    if (!button) return;
    wux::FrameworkElement holder = FindByName(button, L"StreeckStartIcon");
    if (!holder) return;
    wuxm::CompositeTransform transform = holder.RenderTransform().try_as<wuxm::CompositeTransform>();
    if (!transform) return;
    double scale = inside && g_settings.StartHover == 1 ? 1.18 : 1.0;
    double angle = inside && g_settings.StartHover == 2 ? 90.0 : 0.0;
    wuxm::Animation::Storyboard story;
    std::pair<const wchar_t*, double> targets[] = { { L"ScaleX", scale }, { L"ScaleY", scale }, { L"Rotation", angle } };
    for (auto& target : targets)
    {
        wuxm::Animation::DoubleAnimation animation;
        animation.To(target.second);
        animation.Duration(wux::DurationHelper::FromTimeSpan(std::chrono::milliseconds(150)));
        animation.EnableDependentAnimation(true);
        wuxm::Animation::Storyboard::SetTarget(animation, transform);
        wuxm::Animation::Storyboard::SetTargetProperty(animation, target.first);
        story.Children().Append(animation);
    }
    story.Begin();
}

// Clic en Inicio → menú de Streeck. Una capa transparente encima del botón recibe el clic y avisa a la suite por un
// evento con nombre; el clic derecho manda Win + X para que siga saliendo el menú de Windows de siempre.
static void PlaceStartOverlay(FrameState& state, wux::FrameworkElement const& button)
{
    wuxc::Grid root = state.Root.get();
    if (!root) return;
    wuxc::Border overlay = state.StartOverlay.get();
    bool wanted = g_settings.Active && g_settings.StartOpensSuite && button && button.Visibility() == wux::Visibility::Visible;
    if (!wanted)
    {
        if (overlay)
        {
            uint32_t index;
            if (root.Children().IndexOf(overlay, index)) root.Children().RemoveAt(index);
            state.StartOverlay = nullptr;
        }
        return;
    }
    if (!overlay)
    {
        overlay = wuxc::Border();
        overlay.Name(L"StreeckStartClick");
        overlay.Background(wuxm::SolidColorBrush(Argb(0, 0, 0, 0))); // Transparente pero recibe el ratón
        overlay.HorizontalAlignment(wux::HorizontalAlignment::Left);
        overlay.VerticalAlignment(wux::VerticalAlignment::Top);
        wuxc::Grid::SetRowSpan(overlay, 16);
        wuxc::Grid::SetColumnSpan(overlay, 16);
        winrt::weak_ref<wux::FrameworkElement> weakButton = button;
        overlay.Tapped([](auto&&, wux::Input::TappedRoutedEventArgs const& args)
        {
            static HANDLE click = CreateEventW(nullptr, FALSE, FALSE, L"Local\\StreeckSuite.StartClick");
            SetEvent(click);
            args.Handled(true);
        });
        overlay.RightTapped([](auto&&, wux::Input::RightTappedRoutedEventArgs const& args)
        {
            INPUT keys[4] = {};
            WORD codes[4] = { VK_LWIN, 'X', 'X', VK_LWIN };
            for (int i = 0; i < 4; i++)
            {
                keys[i].type = INPUT_KEYBOARD;
                keys[i].ki.wVk = codes[i];
                keys[i].ki.dwFlags = i >= 2 ? KEYEVENTF_KEYUP : 0;
            }
            SendInput(4, keys, sizeof(INPUT));
            args.Handled(true);
        });
        overlay.PointerEntered([weakButton](auto&&, auto&&) { auto b = weakButton.get(); if (b) try { HoverStartIcon(b, true); } catch (...) { } });
        overlay.PointerExited([weakButton](auto&&, auto&&) { auto b = weakButton.get(); if (b) try { HoverStartIcon(b, false); } catch (...) { } });
        root.Children().Append(overlay);
        state.StartOverlay = overlay;
        Log(L"Clic de Inicio: abre el menú de Streeck");
    }
    // Encima del botón (Translation no la cuenta TransformToVisual: se suma a mano)
    Rect r = button.TransformToVisual(root).TransformBounds(Rect{ 0, 0, (float)button.ActualWidth(), (float)button.ActualHeight() });
    double left = r.X + button.Translation().x;
    wux::Thickness margin = overlay.Margin();
    if (std::abs(margin.Left - left) > 0.5 || std::abs(margin.Top - r.Y) > 0.5) overlay.Margin(wux::ThicknessHelper::FromLengths(left, r.Y, 0, 0));
    double width = overlay.Width(), height = overlay.Height(); // NaN mientras no se fijan
    if (std::isnan(width) || std::abs(width - r.Width) > 0.5) overlay.Width(r.Width);
    if (std::isnan(height) || std::abs(height - r.Height) > 0.5) overlay.Height(r.Height);
}

static void ApplyStartIcon(FrameState& state)
{
    wux::FrameworkElement repeater = state.Repeater.get();
    if (!repeater) return;
    wux::FrameworkElement button = state.StartButton.get();
    if (!button)
    {
        button = FindStartButton(repeater);
        if (!button) return;
        state.StartButton = button;
        state.StartDrawn.clear();
    }
    PlaceStartOverlay(state, button);
    wux::FrameworkElement icon = FindByName(button, L"Icon");
    if (!icon) return;
    wuxc::Panel panel = wuxm::VisualTreeHelper::GetParent(icon).try_as<wuxc::Panel>();
    if (!panel) return;

    wux::FrameworkElement current = nullptr;
    for (auto child : panel.Children())
    {
        wux::FrameworkElement element = child.try_as<wux::FrameworkElement>();
        if (element && element.Name() == L"StreeckStartIcon") { current = element; break; }
    }
    bool wanted = g_settings.Active && g_settings.StartIcon != 0;
    std::wstring signature = std::to_wstring(g_settings.StartIcon) + L"|" + std::to_wstring(g_settings.StartSize) + L"|" + g_settings.StartFile;
    if (current && (!wanted || signature != state.StartDrawn))
    {
        uint32_t index;
        if (panel.Children().IndexOf(current, index)) panel.Children().RemoveAt(index);
        current = nullptr;
    }
    if (!wanted)
    {
        if (!state.StartDrawn.empty())
        {
            icon.Opacity(1);
            icon.ClearValue(wux::UIElement::ClipProperty());
            if (wux::FrameworkElement background = FindByName(button, L"BackgroundElement"))
            {
                background.Opacity(1);
                background.ClearValue(wux::UIElement::ClipProperty());
            }
        }
        state.StartDrawn.clear();
        return;
    }
    if (!current)
    {
        double baseSize = icon.ActualWidth() > 4 ? std::min(icon.ActualWidth(), 32.0) : 24.0;
        wux::FrameworkElement holder = MakeStartIcon(baseSize * g_settings.StartSize / 100.0);
        if (wuxc::Grid::GetRow(icon) || wuxc::Grid::GetColumn(icon))
        {
            wuxc::Grid::SetRow(holder, wuxc::Grid::GetRow(icon));
            wuxc::Grid::SetColumn(holder, wuxc::Grid::GetColumn(icon));
        }
        wuxc::Grid::SetRowSpan(holder, wuxc::Grid::GetRowSpan(icon));
        wuxc::Grid::SetColumnSpan(holder, wuxc::Grid::GetColumnSpan(icon));
        panel.Children().Append(holder);
        state.StartDrawn = signature;
        Log(L"Icono de Inicio aplicado (" + signature + L")");
    }
    icon.Opacity(0); // Windows a veces lo vuelve a mostrar al animar el botón: se reaplica en cada pasada
    // Al pasar el ratón, la animación de Windows le devuelve la opacidad un instante (se asomaba el logo arriba de
    // los ojitos): además se recorta a nada, y eso la animación no lo toca
    // El fondo del botón de Windows (BackgroundElement) trae un brillo fino arriba (relieve de Windows 11) que se veía
    // como una rayita sobre los ojitos al pasar el ratón: se oculta igual que el logo
    wux::FrameworkElement background = FindByName(button, L"BackgroundElement");
    for (wux::FrameworkElement part : { icon, background })
    {
        if (!part) continue;
        part.Opacity(0);
        if (!part.Clip())
        {
            wuxm::RectangleGeometry nothing;
            nothing.Rect(winrt::Windows::Foundation::Rect{ 0, 0, 0, 0 });
            part.Clip(nothing);
        }
    }

    if (state.StartHookedButton.get() != button)
    {
        button.PointerEntered([](auto&& sender, auto&&) { try { HoverStartIcon(sender, true); } catch (...) { } });
        button.PointerExited([](auto&& sender, auto&&) { try { HoverStartIcon(sender, false); } catch (...) { } });
        state.StartHookedButton = button;
    }
}

#pragma endregion

static void SetupFrame(std::shared_ptr<FrameState> const& state)
{
    wux::FrameworkElement frame = state->Frame.get();
    if (!frame) return;
    wux::FrameworkElement root = FindByName(frame, L"RootGrid");
    wux::FrameworkElement repeater = FindByName(frame, L"TaskbarFrameRepeater");
    if (!root || !repeater) { Log(L"Barra sin RootGrid o TaskbarFrameRepeater"); return; }
    state->Root = root.as<wuxc::Grid>();
    state->Repeater = repeater;

    // Fondo y borde de Windows de ESTA barra: en las de los otros monitores no siempre llega el aviso del árbol,
    // así que se buscan aquí directamente y se registran como los de la principal
    for (const wchar_t* name : { L"BackgroundFill", L"BackgroundStroke" })
    {
        wux::FrameworkElement background = FindByName(frame, name);
        if (!background || !g_diagnostics) continue;
        InstanceHandle handle = 0;
        if (SUCCEEDED(g_diagnostics->GetHandleFromIInspectable(reinterpret_cast<::IInspectable*>(winrt::get_abi(background)), &handle)) && handle)
        {
            StyleBackground(handle);
        }
    }
    if (!state->Hooked)
    {
        // Cada vez que la barra se reacomoda (apps que abren o cierran, cambios de pantalla) se recolocan las islas
        std::weak_ptr<FrameState> weak = state;
        // Nunca dentro del mismo paso de diseño (XAML lo corta como "ciclo de diseño"): se agenda para justo después
        auto dispatcher = repeater.Dispatcher();
        repeater.LayoutUpdated([weak, dispatcher](auto&&, auto&&)
        {
            auto locked = weak.lock();
            if (!locked) return;
            if (locked->Pending) return;
            locked->Pending = true;
            dispatcher.RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Low, [weak]
            {
                auto state = weak.lock();
                if (!state) return;
                state->Pending = false;
                try { LayoutFrame(*state); } catch (...) { }
            });
        });
        state->Hooked = true;

        // Windows anima los iconos que entran o salen y al terminar no siempre vuelve a acomodar la barra: si la isla
        // se midió a media animación quedaba corta (un icono medio afuera). Se vuelve a medir cada 1,5 s; si nada
        // cambió, la firma evita redibujar
        state->Recheck = wux::DispatcherTimer();
        state->Recheck.Interval(std::chrono::milliseconds(250));
        state->Recheck.Tick([weak](auto&&, auto&&)
        {
            auto locked = weak.lock();
            if (!locked || locked->Pending) return;
            if (locked->Burst > 0) locked->Burst--;
            else if (++locked->Idle < 12) return;
            locked->Idle = 0;
            try { LayoutFrame(*locked); } catch (...) { }
        });
        state->Recheck.Start();
    }
    LayoutFrame(*state);
}

static void DumpTree(const wchar_t* fileName)
{
    std::map<InstanceHandle, std::vector<InstanceHandle>> children;
    std::vector<InstanceHandle> roots;
    for (auto& pair : g_nodes)
    {
        if (g_nodes.count(pair.second.Parent)) children[pair.second.Parent].push_back(pair.first);
        else roots.push_back(pair.first);
    }
    std::wofstream file(DataFolder() + L"\\" + fileName);
    std::vector<std::pair<InstanceHandle, int>> stack;
    for (auto it = roots.rbegin(); it != roots.rend(); ++it) stack.push_back({ *it, 0 });
    while (!stack.empty())
    {
        auto item = stack.back();
        stack.pop_back();
        const Node& node = g_nodes[item.first];
        file << std::wstring(item.second * 2, L' ') << node.Type;
        if (!node.Name.empty()) file << L"#" << node.Name;
        file << L"\n";
        auto& list = children[item.first];
        for (auto it = list.rbegin(); it != list.rend(); ++it) stack.push_back({ *it, item.second + 1 });
    }
    Log(L"Árbol guardado: " + std::to_wstring(g_nodes.size()) + L" piezas");
}

static void ApplyAll()
{
    try
    {
        LoadSettings();
        for (auto& pair : g_originalOpacity) StyleBackground(pair.first);
        Log(L"Barras conocidas: " + std::to_wstring(g_frames.size()));
        for (auto& frame : g_frames)
        {
            frame->Drawn.clear();
            if (frame->Root.get()) LayoutFrame(*frame); else SetupFrame(frame);
        }
        if (ToInt(ReadIni(), L"arbol", 0) == 1) DumpTree();
        Log(g_settings.Active ? L"Islas aplicadas" : L"Barra normal restaurada");
    }
    catch (winrt::hresult_error const& error)
    {
        Log(L"Error al aplicar: " + std::wstring(error.message()));
    }
}

#pragma endregion

#pragma region Árbol visual

struct Watcher : winrt::implements<Watcher, IVisualTreeServiceCallback2>
{
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) noexcept override
    {
        try
        {
            if (mutation == Add)
            {
                Node node;
                node.Type = element.Type ? std::wstring(element.Type, SysStringLen(element.Type)) : L"";
                node.Name = element.Name ? std::wstring(element.Name, SysStringLen(element.Name)) : L"";
                node.Parent = relation.Parent;
                g_nodes[element.Handle] = node;

                if (node.Type == L"Taskbar.TaskbarFrame")
                {
                    wux::FrameworkElement frame = ElementFrom(element.Handle).try_as<wux::FrameworkElement>();
                    if (frame)
                    {
                        if (!g_anchor.get()) g_anchor = frame;
                        auto state = std::make_shared<FrameState>();
                        state->Frame = frame;
                        g_frames.push_back(state);
                        Log(L"Barra encontrada");
                        // Sus hijos aún no existen: se prepara cuando termine de construirse
                        frame.Dispatcher().RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Low, [state] { SetupFrame(state); });
                    }
                }
                if (node.Type == L"Windows.UI.Xaml.Shapes.Rectangle" && (node.Name == L"BackgroundFill" || node.Name == L"BackgroundStroke") &&
                    InsideTaskbarFrame(element.Handle))
                {
                    StyleBackground(element.Handle);
                }
            }
            else if (mutation == Remove)
            {
                g_nodes.erase(element.Handle);
                g_originalOpacity.erase(element.Handle);
                g_frames.erase(std::remove_if(g_frames.begin(), g_frames.end(), [](auto& f) { return !f->Frame.get(); }), g_frames.end());
            }
        }
        catch (...)
        {
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override
    {
        return S_OK;
    }
};

// Espera el aviso de la suite y aplica los ajustes en el hilo de la barra
static void ListenForSettings()
{
    HANDLE changed = CreateEventW(nullptr, FALSE, FALSE, L"Local\\StreeckSuite.TaskbarSkin");
    for (;;)
    {
        WaitForSingleObject(changed, INFINITE);
        wux::UIElement anchor = g_anchor.get();
        if (!anchor) { Log(L"Aviso recibido pero la barra aún no aparece"); continue; }
        anchor.Dispatcher().RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Normal, [] { ApplyAll(); });
    }
}

struct Tap : winrt::implements<Tap, IObjectWithSite>
{
    winrt::com_ptr<IUnknown> m_site;

    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) noexcept override
    {
        try
        {
            m_site.copy_from(site);
            if (!site) return S_OK;
            g_diagnostics = m_site.as<IXamlDiagnostics>();
            winrt::com_ptr<IVisualTreeService3> service = m_site.as<IVisualTreeService3>();
            winrt::com_ptr<Watcher> watcher = winrt::make_self<Watcher>();
            // Igual que TranslucentTB: avisar desde otro hilo evita bloqueos; los avisos llegan al hilo de la barra
            std::thread([service, watcher]
            {
                HRESULT hr = service->AdviseVisualTreeChange(watcher.get());
                Log(SUCCEEDED(hr) ? L"Conectado al árbol de la barra" : L"AdviseVisualTreeChange falló: " + std::to_wstring(hr));
            }).detach();
            std::thread(ListenForSettings).detach();
            StartMedia();
            return S_OK;
        }
        catch (...)
        {
            return winrt::to_hresult();
        }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** site) noexcept override
    {
        if (!m_site) { *site = nullptr; return E_FAIL; }
        return m_site->QueryInterface(riid, site);
    }
};

struct Factory : winrt::implements<Factory, IClassFactory>
{
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** result) noexcept override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        try
        {
            return winrt::make_self<Tap>()->QueryInterface(riid, result);
        }
        catch (...)
        {
            return winrt::to_hresult();
        }
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override
    {
        return S_OK;
    }
};

#pragma endregion

#pragma region Entrada en el Explorador

typedef HRESULT(WINAPI* InitializeXamlDiagnosticsExFn)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, CLSID, LPCWSTR);

// Pide la sesión de diagnóstico XAML para este mismo proceso; reintenta mientras la barra aún no tiene XAML
static DWORD WINAPI Install(void*)
{
    LoadSettings();
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(g_module, path, MAX_PATH);
    HMODULE xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    InitializeXamlDiagnosticsExFn initialize = xaml ? reinterpret_cast<InitializeXamlDiagnosticsExFn>(GetProcAddress(xaml, "InitializeXamlDiagnosticsEx")) : nullptr;
    if (!initialize) { Log(L"No se encontró InitializeXamlDiagnosticsEx"); return 1; }

    HRESULT hr = E_FAIL;
    for (int attempt = 0; attempt < 60; attempt++)
    {
        wchar_t endpoint[64];
        swprintf_s(endpoint, L"VisualDiagConnection%d", attempt % 10 + 1);
        hr = initialize(endpoint, GetCurrentProcessId(), L"", path, CLSID_StreeckTap, nullptr);
        if (SUCCEEDED(hr)) break;
        Sleep(500);
    }
    Log(SUCCEEDED(hr) ? L"Diagnóstico XAML iniciado" : L"InitializeXamlDiagnosticsEx falló: " + std::to_wstring(hr));
    return 0;
}

static bool IsExplorer()
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring exe(path);
    size_t slash = exe.find_last_of(L'\\');
    return _wcsicmp(exe.substr(slash + 1).c_str(), L"explorer.exe") == 0;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    g_module = instance;
    DisableThreadLibraryCalls(instance);
    if (IsExplorer())
    {
        // Solo una copia activa por Explorador: si ya entró otra versión, esta no hace nada
        HANDLE once = CreateMutexW(nullptr, FALSE, L"Local\\StreeckSuite.TaskbarSkin.Loaded");
        if (once && GetLastError() == ERROR_ALREADY_EXISTS) return TRUE;
        // Fijada: el gancho que la trajo se quita enseguida y el árbol XAML guarda punteros a nuestro código
        HMODULE pinned;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, reinterpret_cast<LPCWSTR>(&DllMain), &pinned);
        HANDLE thread = CreateThread(nullptr, 0, Install, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}

#pragma endregion

// Exportaciones: la clase TAP para Windows y un gancho vacío que solo sirve para entrar en el Explorador
STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** result)
{
    if (clsid != CLSID_StreeckTap) return CLASS_E_CLASSNOTAVAILABLE;
    try
    {
        return winrt::make_self<Factory>()->QueryInterface(riid, result);
    }
    catch (...)
    {
        return winrt::to_hresult();
    }
}

STDAPI DllCanUnloadNow()
{
    return S_FALSE;
}

extern "C" LRESULT CALLBACK StreeckHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    return CallNextHookEx(nullptr, code, wParam, lParam);
}
