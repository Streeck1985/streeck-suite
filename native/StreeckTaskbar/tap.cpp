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
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.Media.Control.h>
#include <winrt/Windows.Storage.Streams.h>
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

// Colores del estilo de cristal global (los mismos de Glass.cs)
static Color FillColor()
{
    switch (g_settings.Glass)
    {
        case Transparent: return Argb(70, 18, 22, 32);
        case Obsidian: return Argb(240, 6, 8, 12);
        case Neon: return Argb(215, 14, 18, 28);
        default: return Argb(225, 14, 18, 28);
    }
}

static Color BorderColor()
{
    switch (g_settings.Glass)
    {
        case Transparent: return Argb(25, 255, 255, 255);
        case Obsidian: return Argb(22, 255, 255, 255);
        case Neon: return Argb(210, 56, 189, 248);
        default: return Argb(41, 255, 255, 255);
    }
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

// Isla con su sombra (o halo azul en Neón), relleno y borde
static void AddIsland(wuxc::Canvas const& canvas, double x, double y, double w, double h, double radius)
{
    if (g_settings.Glass == Neon)
    {
        AddRect(canvas, x - 3, y - 3, w + 6, h + 6, radius + 3, Argb(28, 56, 189, 248), Color{}, 0);
        AddRect(canvas, x - 1.5, y - 1.5, w + 3, h + 3, radius + 1.5, Argb(50, 56, 189, 248), Color{}, 0);
    }
    else if (g_settings.Glass != Transparent)
    {
        AddRect(canvas, x - 2, y - 1, w + 4, h + 4, radius + 2, Argb(30, 0, 0, 0), Color{}, 0);
        AddRect(canvas, x - 1, y, w + 2, h + 2, radius + 1, Argb(45, 0, 0, 0), Color{}, 0);
    }
    AddRect(canvas, x, y, w, h, radius, FillColor(), BorderColor(), g_settings.Glass == Neon ? 1.5 : 1.0);
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

static void LayoutFrame(FrameState& state)
{
    wuxc::Grid root = state.Root.get();
    wux::FrameworkElement repeater = state.Repeater.get();
    if (!root || !repeater) return;

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
        if (group == 0)
        {
            double left = 1e9, right = -1e9;
            VisibleExtent(element, root, left, right, element.Translation().x);
            if (left < right) { bounds.X = (float)left; bounds.Width = (float)(right - left); }
        }
        auto shift = g_shift.find(winrt::get_abi(element));
        if (shift != g_shift.end()) bounds.X -= shift->second;  // Posición natural, sin nuestro desplazamiento
        // ItemsRepeater guarda los botones que recicla fuera de la pantalla (x = -10000): no cuentan
        if (bounds.X + bounds.Width < -200 || bounds.X > 20000) continue;
        items.push_back({ element, group, bounds });
        minX[group] = std::min(minX[group], (double)bounds.X);
        maxX[group] = std::max(maxX[group], (double)(bounds.X + bounds.Width));
    }

    const double width = root.ActualWidth(), height = root.ActualHeight();
    const double pad = 8, islandHeight = std::min(height - 6, 40.0), top = (height - islandHeight) / 2;
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
    bool centered = g_settings.Active && g_settings.Format != Full && !(g_settings.Format == Islands && g_settings.Edges);
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
        state.TrayWidth = islandWidth[2];
        state.TrayInset = present[2] ? width - (maxX[2] + pad) : 0;
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
    if (!g_frames.empty() && g_frames[0].get() == &state && present[1])
    {
        PlacePlayer(root, maxX[1] + shiftOf[1] + pad + 14, top, islandHeight);
    }

    // Firma de lo que hay que dibujar
    wchar_t signature[256];
    swprintf_s(signature, L"%d|%d|%d|%d|%d|%.0f|%.0f|%.0f,%.0f|%.0f,%.0f|%.0f,%.0f", g_settings.Active, g_settings.Format, g_settings.Gap,
        g_settings.Edges, g_settings.Glass, width, height, minX[0] + shiftOf[0], islandWidth[0], minX[1] + shiftOf[1], islandWidth[1],
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

    if (g_settings.Format == Full)
    {
        AddRect(canvas, 0, 0, width, height, 0, FillColor(), Color{}, 0);
        AddRect(canvas, 0, 0, width, 1, 0, BorderColor(), Color{}, 0);
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
        AddIsland(canvas, left, top, right - left, islandHeight, islandHeight / 2);
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
        if (present[g]) AddIsland(canvas, minX[g] + shiftOf[g] - pad, top, islandWidth[g], islandHeight, islandHeight / 2);
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
static void PlacePlayer(wuxc::Grid const& root, double x, double top, double height)
{
    bool wanted = g_settings.Active && g_settings.MiniPlayer && g_hasMedia && g_settings.Format != Capsule;
    if (!wanted) { RemovePlayer(root); return; }
    wuxc::Canvas canvas = EnsurePlayer(root);
    wuxc::Border island = g_playerIsland.get();
    if (!island) return;
    island.Height(height);
    island.Background(wuxm::SolidColorBrush(g_settings.Format == Full ? Color{ 0, 0, 0, 0 } : FillColor()));
    island.BorderBrush(wuxm::SolidColorBrush(g_settings.Format == Full ? Color{ 0, 0, 0, 0 } : BorderColor()));
    island.BorderThickness(wux::Thickness{ 1, 1, 1, 1 });
    if (std::abs(g_playerX - x) > 1)
    {
        wuxc::Canvas::SetLeft(island, x);
        wuxc::Canvas::SetTop(island, top);
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

static void SetupFrame(std::shared_ptr<FrameState> const& state)
{
    wux::FrameworkElement frame = state->Frame.get();
    if (!frame) return;
    wux::FrameworkElement root = FindByName(frame, L"RootGrid");
    wux::FrameworkElement repeater = FindByName(frame, L"TaskbarFrameRepeater");
    if (!root || !repeater) { Log(L"Barra sin RootGrid o TaskbarFrameRepeater"); return; }
    state->Root = root.as<wuxc::Grid>();
    state->Repeater = repeater;
    if (!state->Hooked)
    {
        // Cada vez que la barra se reacomoda (apps que abren o cierran, cambios de pantalla) se recolocan las islas
        std::weak_ptr<FrameState> weak = state;
        // Nunca dentro del mismo paso de diseño (XAML lo corta como "ciclo de diseño"): se agenda para justo después
        auto dispatcher = repeater.Dispatcher();
        repeater.LayoutUpdated([weak, dispatcher](auto&&, auto&&)
        {
            auto locked = weak.lock();
            if (!locked || locked->Pending) return;
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
    }
    LayoutFrame(*state);
}

static void DumpTree()
{
    std::map<InstanceHandle, std::vector<InstanceHandle>> children;
    std::vector<InstanceHandle> roots;
    for (auto& pair : g_nodes)
    {
        if (g_nodes.count(pair.second.Parent)) children[pair.second.Parent].push_back(pair.first);
        else roots.push_back(pair.first);
    }
    std::wofstream file(DataFolder() + L"\\barra_arbol.txt");
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
